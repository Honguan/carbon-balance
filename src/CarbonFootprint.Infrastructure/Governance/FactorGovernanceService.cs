using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Units;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class FactorGovernanceService(
    CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IOrganizationScope scope) : IFactorGovernance
{
    private const string ResourceType = "EmissionFactorVersion";

    public async Task<OperationResult> ReviewAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        var (failure, factor) = await BeginAsync(context, factorVersionId, rowVersion, "ReviewFactor", cancellationToken);
        if (failure is not null) return failure;
        if (factor!.PublicationStatus != FactorPublicationStatus.Draft.ToString()) return OperationResult.BadRequest;

        factor.ReviewStatus = FactorReviewStatus.Approved.ToString();
        factor.ReviewedAt = DateTimeOffset.UtcNow;
        factor.ReviewedBy = context.ActorId;
        Audit(context, "factor.version.reviewed", factor.Id);
        return await SaveAsync(context, "ReviewFactor", factor.Id, OperationResult.Success(), cancellationToken);
    }

    public async Task<OperationResult> PublishAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        var (failure, factor) = await BeginAsync(context, factorVersionId, rowVersion, "PublishFactor", cancellationToken);
        if (failure is not null) return failure;
        if (!string.Equals(factor!.PublicationStatus, FactorPublicationStatus.Draft.ToString(), StringComparison.Ordinal)
            || factor.ReviewStatus != FactorReviewStatus.Approved.ToString())
        {
            return OperationResult.Invalid("factor", "只有草稿係數版本可發布。");
        }

        var publishedPredecessors = await db.EmissionFactorVersions
            .Where(item => item.FactorId == factor.FactorId && item.Id != factor.Id
                && item.PublicationStatus == FactorPublicationStatus.Published.ToString())
            .ToArrayAsync(cancellationToken);
        foreach (var predecessor in publishedPredecessors)
        {
            predecessor.PublicationStatus = FactorPublicationStatus.Withdrawn.ToString();
            predecessor.WithdrawnAt = DateTimeOffset.UtcNow;
            Audit(context, "factor.version.withdrawn", predecessor.Id);
        }

        factor.PublicationStatus = FactorPublicationStatus.Published.ToString();
        factor.PublishedAt = DateTimeOffset.UtcNow;
        Audit(context, "factor.version.published", factor.Id);
        return await SaveAsync(context, "PublishFactor", factor.Id, OperationResult.Success("係數版本已發布。"), cancellationToken);
    }

    public async Task<OperationResult> WithdrawAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        var (failure, factor) = await BeginAsync(context, factorVersionId, rowVersion, "WithdrawFactor", cancellationToken);
        if (failure is not null) return failure;
        if (!string.Equals(factor!.PublicationStatus, FactorPublicationStatus.Published.ToString(), StringComparison.Ordinal))
        {
            return OperationResult.Invalid("factor", "只有已發布係數版本可撤回。");
        }

        factor.PublicationStatus = FactorPublicationStatus.Withdrawn.ToString();
        factor.WithdrawnAt = DateTimeOffset.UtcNow;
        Audit(context, "factor.version.withdrawn", factor.Id);
        return await SaveAsync(context, "WithdrawFactor", factor.Id, OperationResult.Success("係數版本已撤回；歷史計算不受影響。"), cancellationToken);
    }

    public async Task<OperationResult> CreateAsync(OperationContext context, FactorInput input, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: false)) return OperationResult.Forbidden;
        var hasSourceType = TryResolveControlledValue(input.SourceType, input.SourceTypeOther, out var sourceType);
        var hasGeography = TryResolveControlledValue(input.Geography, input.GeographyOther, out var geography);
        if (string.IsNullOrWhiteSpace(input.Name)
            || input.Value is null or < 0m
            || !hasSourceType
            || !hasGeography
            || input.ValidFrom > input.ValidTo
            || string.IsNullOrWhiteSpace(input.SourceDatasetVersion)
            || string.IsNullOrWhiteSpace(input.LicenseCode)
            || string.IsNullOrWhiteSpace(input.SourceName)
            || string.IsNullOrWhiteSpace(input.SourceReference)
            || string.IsNullOrWhiteSpace(input.DatasetName)
            || string.IsNullOrWhiteSpace(input.OriginalDocumentName)
            || string.IsNullOrWhiteSpace(input.Applicability))
        {
            return OperationResult.Invalid("factor", "來源類型、地域、有效期間與原始文件皆為必填。");
        }
        if (!await db.Units.AnyAsync(item => item.CatalogueVersion == UnitCatalogue.CurrentVersion && item.Code == input.DenominatorUnitCode, cancellationToken))
        {
            return OperationResult.Invalid("factor", "係數分母必須使用受控單位。");
        }

        var factorVersionId = Guid.NewGuid();
        db.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
        {
            Id = factorVersionId,
            OrganizationId = OrganizationId,
            FactorId = Guid.NewGuid(),
            VersionNumber = 1,
            Name = input.Name.Trim(),
            Value = input.Value.Value,
            NumeratorUnitCode = "kgCO2e",
            DenominatorUnitCode = input.DenominatorUnitCode!,
            Geography = geography,
            ValidFrom = input.ValidFrom,
            ValidTo = input.ValidTo,
            PublicationStatus = FactorPublicationStatus.Draft.ToString(),
            SourceDatasetVersion = input.SourceDatasetVersion.Trim(),
            LicenseCode = input.LicenseCode.Trim(),
            SourceType = sourceType,
            SourceName = input.SourceName.Trim(),
            SourceReference = input.SourceReference.Trim(),
            DatasetName = input.DatasetName.Trim(),
            OriginalDocumentName = input.OriginalDocumentName.Trim(),
            OriginalDocumentSha256 = string.Empty,
            Applicability = input.Applicability.Trim(),
            ReviewStatus = FactorReviewStatus.Pending.ToString()
        });
        Audit(context, "factor.version.created", factorVersionId);
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("係數草稿已建立；發布後才可用於新計算。");
    }

    public async Task<OperationResult> SupersedeAsync(
        OperationContext context, Guid factorVersionId, FactorSupersedeInput input, uint rowVersion, CancellationToken cancellationToken)
    {
        var (failure, current) = await BeginAsync(context, factorVersionId, rowVersion, "SupersedeFactor", cancellationToken);
        if (failure is not null) return failure;
        if (current!.PublicationStatus != FactorPublicationStatus.Published.ToString()
            || input.Value is null or < 0m
            || string.IsNullOrWhiteSpace(input.SourceDatasetVersion)
            || string.IsNullOrWhiteSpace(input.SourceReference)
            || string.IsNullOrWhiteSpace(input.OriginalDocumentName)
            || string.IsNullOrWhiteSpace(input.Applicability))
        {
            return OperationResult.Invalid("factor", "更新已發布係數時，數值、來源版本、來源網址、原始文件與適用性皆須有效。");
        }

        var newVersionId = Guid.NewGuid();
        db.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
        {
            Id = newVersionId,
            OrganizationId = current.OrganizationId,
            FactorId = current.FactorId,
            VersionNumber = current.VersionNumber + 1,
            Name = current.Name,
            Value = input.Value.Value,
            NumeratorUnitCode = current.NumeratorUnitCode,
            DenominatorUnitCode = current.DenominatorUnitCode,
            Geography = current.Geography,
            ValidFrom = current.ValidFrom,
            ValidTo = current.ValidTo,
            PublicationStatus = FactorPublicationStatus.Draft.ToString(),
            SourceDatasetVersion = input.SourceDatasetVersion.Trim(),
            LicenseCode = current.LicenseCode,
            SourceType = current.SourceType,
            SourceName = current.SourceName,
            SourceReference = input.SourceReference.Trim(),
            DatasetName = current.DatasetName,
            OriginalDocumentName = input.OriginalDocumentName.Trim(),
            OriginalDocumentSha256 = string.Empty,
            Applicability = input.Applicability.Trim(),
            ReviewStatus = FactorReviewStatus.Pending.ToString(),
            SupersedesVersionId = current.Id
        });
        Audit(context, "factor.version.superseded", newVersionId);
        return await SaveAsync(context, "SupersedeFactor", current.Id,
            OperationResult.Success("更新草稿已建立；現行係數會保留至新版本審查發布，歷史計算不受影響。"), cancellationToken);
    }

    private static bool TryResolveControlledValue(string? selected, string? other, out string value)
    {
        value = selected?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!string.Equals(value, "__other__", StringComparison.Ordinal)) return true;
        value = other?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private async Task<(OperationResult? Failure, EmissionFactorVersionRecord? Factor)> BeginAsync(
        OperationContext context, Guid factorVersionId, uint rowVersion, string operation, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: true)) return (OperationResult.Forbidden, null);
        var factor = await db.EmissionFactorVersions.SingleOrDefaultAsync(item => item.Id == factorVersionId, cancellationToken);
        if (factor is null) return (OperationResult.NotFound, null);
        if (rowVersion != factor.RowVersion) return (await StaleAsync(context, operation, factor.Id, cancellationToken), null);
        return (null, factor);
    }

    private Guid OrganizationId => scope.OrganizationId ?? throw new InvalidOperationException("缺少目前組織範圍。");

    private void Audit(OperationContext context, string action, Guid id) =>
        OperationAudit.AppendTracked(db, context, OrganizationId, action, ResourceType, id);

    private async Task<OperationResult> StaleAsync(OperationContext context, string operation, Guid id, CancellationToken cancellationToken)
    {
        await OperationAudit.RecordStaleRejectionAsync(db, context, OrganizationId, operation, ResourceType, id, cancellationToken);
        return OperationResult.Conflict;
    }

    private async Task<OperationResult> SaveAsync(
        OperationContext context, string operation, Guid id, OperationResult success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await StaleAsync(context, operation, id, cancellationToken);
        }
    }
}
