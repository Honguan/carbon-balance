namespace CarbonFootprint.Domain.Modules.Allocations;

public enum AllocationMethod
{
    Mass = 1,
    EnergyContent = 2,
    EconomicValue = 3,
    ProductionTime = 4,
    EquipmentRuntime = 5,
    FloorArea = 6,
    DirectMeasurement = 7,
    SystemExpansion = 8,
    PcrDefinedCustom = 9
}

public enum AllocationPoolStatus
{
    Draft = 1,
    Approved = 2,
    Superseded = 3,
    Withdrawn = 4
}

public sealed record AllocationOutput(
    Guid ProductVersionId,
    string Name,
    decimal BasisValue,
    string BasisUnit,
    bool IsCoProduct,
    bool IsByProduct,
    decimal? EconomicValue,
    string Currency,
    DateOnly? ValuationDate,
    IReadOnlyList<Guid> EvidenceDocumentVersionIds);

public sealed record AllocationPoolVersion(
    Guid Id,
    Guid PoolId,
    int VersionNumber,
    Guid OrganizationId,
    Guid FacilityId,
    Guid? ProcessId,
    Guid SourceActivityId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    AllocationMethod Method,
    decimal TotalResourceQuantity,
    string ResourceUnit,
    string FormulaVersion,
    string CalculationBasis,
    IReadOnlyList<AllocationOutput> Outputs,
    AllocationPoolStatus Status,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    Guid? SupersedesVersionId = null)
{
    public bool IsImmutable => Status is AllocationPoolStatus.Approved
        or AllocationPoolStatus.Superseded
        or AllocationPoolStatus.Withdrawn;
}

public sealed record AllocationShare(
    Guid ProductVersionId,
    decimal BasisValue,
    decimal Share,
    decimal AllocatedResourceQuantity,
    string ResourceUnit);

public sealed record AllocationResult(
    Guid PoolVersionId,
    string FormulaVersion,
    decimal Denominator,
    IReadOnlyList<AllocationShare> Shares,
    DateTimeOffset CalculatedAt,
    string CanonicalTrace)
{
    public decimal ShareTotal => Shares.Sum(item => item.Share);
}

public sealed record AllocationValidationError(
    string Code,
    string EntityKey,
    string Message);

public static class AllocationPoolCalculator
{
    public static IReadOnlyList<AllocationValidationError> Validate(
        AllocationPoolVersion pool,
        decimal shareTolerance = 0.000001m)
    {
        ArgumentNullException.ThrowIfNull(pool);
        var errors = new List<AllocationValidationError>();
        var key = pool.Id.ToString("D");

        if (pool.PeriodStart > pool.PeriodEnd)
        {
            errors.Add(new("ALLOC-PERIOD", key, "Allocation period start cannot be later than the end date."));
        }

        if (pool.TotalResourceQuantity <= 0m)
        {
            errors.Add(new("ALLOC-RESOURCE", key, "Total shared resource quantity must be positive."));
        }

        if (string.IsNullOrWhiteSpace(pool.ResourceUnit))
        {
            errors.Add(new("ALLOC-RESOURCE-UNIT", key, "Shared resource unit is required."));
        }

        if (pool.Outputs.Count < 2)
        {
            errors.Add(new("ALLOC-OUTPUT-COUNT", key, "An allocation pool must contain at least two outputs."));
        }

        if (pool.Outputs.Select(item => item.ProductVersionId).Distinct().Count() != pool.Outputs.Count)
        {
            errors.Add(new("ALLOC-DUPLICATE-OUTPUT", key, "The same product cannot appear twice in one allocation pool version."));
        }

        foreach (var output in pool.Outputs)
        {
            var outputKey = output.ProductVersionId.ToString("D");
            if (output.BasisValue < 0m)
            {
                errors.Add(new("ALLOC-BASIS-NEGATIVE", outputKey, "Allocation basis value cannot be negative."));
            }

            if (string.IsNullOrWhiteSpace(output.BasisUnit))
            {
                errors.Add(new("ALLOC-BASIS-UNIT", outputKey, "Allocation basis unit is required."));
            }

            ValidateMethodSpecific(pool, output, errors);
        }

        var denominator = pool.Outputs.Sum(item => item.BasisValue);
        if (pool.Method != AllocationMethod.SystemExpansion && denominator <= 0m)
        {
            errors.Add(new("ALLOC-DENOMINATOR", key, "Allocation denominator must be positive."));
        }

        if (errors.Count == 0 && pool.Method != AllocationMethod.SystemExpansion)
        {
            var shareTotal = pool.Outputs.Sum(output => output.BasisValue / denominator);
            if (Math.Abs(shareTotal - 1m) > Math.Abs(shareTolerance))
            {
                errors.Add(new("ALLOC-SHARE-TOTAL", key, "Allocation shares do not sum to 100 percent within tolerance."));
            }
        }

        return errors
            .OrderBy(error => error.Code, StringComparer.Ordinal)
            .ThenBy(error => error.EntityKey, StringComparer.Ordinal)
            .ToArray();
    }

    public static AllocationResult Calculate(
        AllocationPoolVersion pool,
        DateTimeOffset calculatedAt,
        decimal shareTolerance = 0.000001m)
    {
        var errors = Validate(pool, shareTolerance);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors.Select(error => $"{error.Code}: {error.Message}")));
        }

        if (pool.Method == AllocationMethod.SystemExpansion)
        {
            throw new InvalidOperationException("System expansion must be represented by explicit avoided-burden calculation lines, not percentage allocation shares.");
        }

        var denominator = pool.Outputs.Sum(item => item.BasisValue);
        var shares = pool.Outputs
            .OrderBy(item => item.ProductVersionId)
            .Select(output =>
            {
                var share = output.BasisValue / denominator;
                return new AllocationShare(
                    output.ProductVersionId,
                    output.BasisValue,
                    share,
                    pool.TotalResourceQuantity * share,
                    pool.ResourceUnit);
            })
            .ToArray();

        var trace = string.Join(
            "|",
            new[]
            {
                $"pool={pool.Id:D}",
                $"version={pool.VersionNumber}",
                $"method={pool.Method}",
                $"formula={pool.FormulaVersion}",
                $"resource={pool.TotalResourceQuantity:G29}:{pool.ResourceUnit}",
                $"denominator={denominator:G29}"
            }.Concat(shares.Select(share =>
                $"output={share.ProductVersionId:D},{share.BasisValue:G29},{share.Share:G29},{share.AllocatedResourceQuantity:G29}")));

        return new AllocationResult(
            pool.Id,
            pool.FormulaVersion,
            denominator,
            shares,
            calculatedAt,
            trace);
    }

    public static bool InvalidatesCalculation(
        AllocationPoolVersion previous,
        AllocationPoolVersion current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (previous.PoolId != current.PoolId)
        {
            throw new InvalidOperationException("Allocation versions must belong to the same pool.");
        }

        return previous.Method != current.Method
            || previous.TotalResourceQuantity != current.TotalResourceQuantity
            || !string.Equals(previous.ResourceUnit, current.ResourceUnit, StringComparison.Ordinal)
            || !string.Equals(previous.FormulaVersion, current.FormulaVersion, StringComparison.Ordinal)
            || !string.Equals(previous.CalculationBasis, current.CalculationBasis, StringComparison.Ordinal)
            || !OutputsEqual(previous.Outputs, current.Outputs);
    }

    private static void ValidateMethodSpecific(
        AllocationPoolVersion pool,
        AllocationOutput output,
        ICollection<AllocationValidationError> errors)
    {
        var outputKey = output.ProductVersionId.ToString("D");
        if (pool.Method == AllocationMethod.EconomicValue)
        {
            if (output.EconomicValue is null or <= 0m)
            {
                errors.Add(new("ALLOC-ECONOMIC-VALUE", outputKey, "Economic allocation requires a positive economic value for every output."));
            }

            if (string.IsNullOrWhiteSpace(output.Currency))
            {
                errors.Add(new("ALLOC-CURRENCY", outputKey, "Economic allocation requires a currency."));
            }

            if (output.ValuationDate is null)
            {
                errors.Add(new("ALLOC-VALUATION-DATE", outputKey, "Economic allocation requires a valuation date or period."));
            }

            if (output.EvidenceDocumentVersionIds.Count == 0)
            {
                errors.Add(new("ALLOC-ECONOMIC-EVIDENCE", outputKey, "Economic allocation requires price evidence."));
            }
        }

        if (pool.Method is AllocationMethod.DirectMeasurement or AllocationMethod.PcrDefinedCustom)
        {
            if (string.IsNullOrWhiteSpace(pool.CalculationBasis))
            {
                errors.Add(new("ALLOC-CUSTOM-BASIS", pool.Id.ToString("D"), "Direct measurement and custom allocation require a documented calculation basis."));
            }

            if (output.EvidenceDocumentVersionIds.Count == 0)
            {
                errors.Add(new("ALLOC-CUSTOM-EVIDENCE", outputKey, "Direct measurement and custom allocation require supporting evidence."));
            }
        }
    }

    private static bool OutputsEqual(
        IReadOnlyList<AllocationOutput> left,
        IReadOnlyList<AllocationOutput> right)
    {
        var orderedLeft = left.OrderBy(item => item.ProductVersionId).ToArray();
        var orderedRight = right.OrderBy(item => item.ProductVersionId).ToArray();
        if (orderedLeft.Length != orderedRight.Length)
        {
            return false;
        }

        for (var index = 0; index < orderedLeft.Length; index++)
        {
            if (orderedLeft[index] != orderedRight[index])
            {
                return false;
            }
        }

        return true;
    }
}
