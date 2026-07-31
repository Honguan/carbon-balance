using System.Text;
using CarbonFootprint.Domain.Modules.Allocations;
using CarbonFootprint.Domain.Modules.DataQuality;
using CarbonFootprint.Domain.Modules.Evidence;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Formulas;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Readiness;
using CarbonFootprint.Domain.Modules.Transport;
using CarbonFootprint.Domain.Modules.Verification;

namespace CarbonFootprint.Unit.Tests;

public sealed class AdvancedInventoryGovernanceTests
{
    [Fact]
    public void ReadinessValidator_BlocksIncompleteSubmissionAndRequiresAcknowledgements()
    {
        var activityId = Guid.NewGuid();
        var context = new InventoryReadinessContext(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 31),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            180,
            true,
            true,
            new HashSet<LifecycleStage> { LifecycleStage.RawMaterial, LifecycleStage.Manufacturing },
            new[]
            {
                new ReadinessActivityContext(
                    activityId,
                    LifecycleStage.RawMaterial,
                    "owner-a",
                    new DateOnly(2025, 1, 1),
                    new DateOnly(2025, 12, 31),
                    80m,
                    true,
                    "supplier data unavailable",
                    false,
                    string.Empty,
                    new ReadinessFactorContext(Guid.NewGuid(), true, true, true, true, true),
                    new[]
                    {
                        new ReadinessEvidenceContext(Guid.NewGuid(), true, true, true, true)
                    })
            },
            Array.Empty<ReadinessAllocationContext>(),
            0.25m,
            string.Empty,
            string.Empty,
            true,
            true,
            true,
            new HashSet<string>());

        var report = InventoryReadinessValidator.Validate(context);

        Assert.Contains(report.Results, result => result.Code == "INV-STAGE-REQUIRED");
        Assert.Contains(report.Results, result => result.Code == "INV-ESTIMATED-SHARE");
        Assert.False(report.CanSubmit(new HashSet<string>()));
    }

    [Fact]
    public void DataQuality_AssessmentIsVersionedAndDeterministic()
    {
        var ruleSetId = Guid.NewGuid();
        var rules = new DataQualityRuleSetVersion(
            ruleSetId,
            "dq-v1",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Enum.GetValues<DataQualityDimension>()
                .Select(dimension => new DataQualityCriterion(dimension, 1, 1m, $"{dimension}-1", "criterion"))
                .ToArray(),
            true);
        var assessment = new DataQualityAssessmentVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ruleSetId,
            DataSourceCategory.PrimaryMeasured,
            "meter",
            "assessor",
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            Enum.GetValues<DataQualityDimension>()
                .Select((dimension, index) => new DataQualityDimensionScore(
                    dimension,
                    index + 1,
                    $"{dimension}-{index + 1}",
                    "documented",
                    Array.Empty<Guid>()))
                .ToArray(),
            "complete assessment");

        var score = assessment.CalculateOverallScore(rules);
        var firstHash = assessment.CreateCanonicalHash(score);
        var secondHash = assessment.CreateCanonicalHash(score);

        Assert.Equal(3m, score);
        Assert.Equal(firstHash, secondHash);
        Assert.Equal(64, firstHash.Length);
    }

    [Fact]
    public void UncertaintyAnalysis_RanksSensitiveInputsAndIsRepeatable()
    {
        var inputs = new[]
        {
            new UncertaintyInput(
                Guid.NewGuid(), "electricity", 100m, UncertaintyDistribution.Uniform,
                90m, 110m, null, 50m, DataSourceCategory.PrimaryMeasured),
            new UncertaintyInput(
                Guid.NewGuid(), "transport", 100m, UncertaintyDistribution.Triangular,
                50m, 150m, null, 20m, DataSourceCategory.Proxy)
        };

        var first = UncertaintyAnalysisService.Analyze(inputs, simulationIterations: 500, seed: 42);
        var second = UncertaintyAnalysisService.Analyze(inputs, simulationIterations: 500, seed: 42);

        Assert.Equal(first.LowerResult, second.LowerResult);
        Assert.Equal(first.UpperResult, second.UpperResult);
        Assert.Equal("transport", first.Sensitivities[0].Name);
        Assert.Equal(70m, first.BaseResult);
    }

    [Fact]
    public void AllocationPool_ComputesAuditableShares()
    {
        var pool = new AllocationPoolVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            AllocationMethod.Mass,
            1000m,
            "kWh",
            "allocation-v1",
            "mass output",
            new[]
            {
                Output(100m),
                Output(300m)
            },
            AllocationPoolStatus.Approved,
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            "owner");

        var result = AllocationPoolCalculator.Calculate(pool, DateTimeOffset.Parse("2026-07-31T01:00:00Z"));

        Assert.Equal(1m, result.ShareTotal);
        Assert.Equal(250m, result.Shares[0].AllocatedResourceQuantity);
        Assert.Equal(750m, result.Shares[1].AllocatedResourceQuantity);
        Assert.Contains("denominator=400", result.CanonicalTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void FormulaRegistry_ExecutesPublishedImplementationWithoutSwitchLogic()
    {
        var definition = new ActivityFormulaDefinitionVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "factor-emission-v1",
            ActivityCategory.PurchasedElectricity,
            FormulaCalculationStrategy.FactorBased,
            FormulaPublicationStatus.Published,
            new[]
            {
                Input("activityAmount", "kWh"),
                Input("emissionFactor", "kgCO2e/kWh"),
                Input("allocationFactor", "ratio", false)
            },
            "emissions",
            "kgCO2e",
            FactorBasedFormula.Key,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "admin",
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        var registry = new ActivityFormulaRegistry(
            new IActivityFormulaImplementation[] { new FactorBasedFormula() },
            new[] { definition });
        var context = new FormulaExecutionContext(
            Guid.NewGuid(),
            definition,
            new Dictionary<string, FormulaValue>
            {
                ["activityAmount"] = Value("activityAmount", 100m, "kWh"),
                ["emissionFactor"] = Value("emissionFactor", 0.5m, "kgCO2e/kWh"),
                ["allocationFactor"] = Value("allocationFactor", 0.8m, "ratio")
            },
            new Dictionary<string, string>(),
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"));

        var result = registry.Execute(context);

        Assert.Equal(40m, result.Result);
        Assert.Equal("kgCO2e", result.Unit);
        Assert.Contains("implementation=factor-based-v1", result.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void TransportChain_CalculatesOrderedMultiLegRoute()
    {
        var chain = new TransportChainVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "inbound route",
            new[]
            {
                Leg(1, TransportMode.Road, 100m, 2m, 0.5m),
                Leg(2, TransportMode.Sea, 1000m, 2m, 0.01m)
            },
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            "owner",
            false,
            null);

        var result = TransportChainCalculator.Calculate(chain);

        Assert.Equal(2, result.Legs.Count);
        Assert.Equal(220m, result.TotalEmissions);
        Assert.Equal(1, result.Legs[0].Sequence);
        Assert.Equal(2, result.Legs[1].Sequence);
    }

    [Fact]
    public void GlobalFactorSync_IsIdempotentAndUsesStableIdentifiers()
    {
        var empty = new GlobalFactorCatalogSnapshot(
            Array.Empty<GlobalFactor>(),
            Array.Empty<GlobalFactorVersion>(),
            Array.Empty<GlobalFactorAlias>(),
            Array.Empty<GlobalFactorImportBatch>());
        var source = new[]
        {
            new OfficialFactorSourceRecord(
                "MOENV-001", "MOENV", "CFP_P_02", "Grid electricity", 0.5m,
                "kgCO2e", "kWh", "TW", "grid", null, null,
                new DateOnly(2026, 1, 1), "2026", "open", "https://example.test/factor",
                new string('a', 64), false)
        };
        var first = GlobalFactorCatalogService.Synchronize(
            empty, source, new string('b', 64), DateTimeOffset.Parse("2026-07-31T00:00:00Z"));
        var second = GlobalFactorCatalogService.Synchronize(
            first.Catalog, source, new string('b', 64), DateTimeOffset.Parse("2026-07-31T01:00:00Z"));

        Assert.Single(first.AddedFactorIds);
        Assert.Single(first.AddedVersionIds);
        Assert.Empty(second.AddedFactorIds);
        Assert.Empty(second.AddedVersionIds);
        Assert.Equal(
            GlobalFactorCatalogService.BuildStableKey(source[0]),
            first.Catalog.Factors[0].StableSourceKey);
    }

    [Fact]
    public void EvidenceService_ComputesHashAndDeduplicatesPhysicalStorage()
    {
        var empty = EmptyEvidenceRepository();
        var bytes = Encoding.UTF8.GetBytes("invoice-content");
        var scan = new EvidenceScanResult(
            EvidenceScanStatus.Clean,
            "ClamAV",
            "1.5",
            "daily",
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            "clean");
        var first = EvidenceDocumentService.Upload(
            empty,
            UploadRequest(bytes, scan, null, "invoice-a.pdf"));
        var second = EvidenceDocumentService.Upload(
            first.Repository,
            UploadRequest(bytes, scan, null, "invoice-b.pdf"));

        Assert.True(first.Version.Hash.IsVerifiedSha256);
        Assert.False(first.ReusedPhysicalObject);
        Assert.True(second.ReusedPhysicalObject);
        Assert.Equal(first.Version.ObjectStorageKey, second.Version.ObjectStorageKey);
        Assert.NotEqual(first.Document.Id, second.Document.Id);
    }

    [Fact]
    public void VerificationWorkflow_EnforcesMfaAndSeparationOfDuties()
    {
        var projectId = Guid.NewGuid();
        var creator = new WorkflowActor(
            "creator",
            Guid.NewGuid(),
            new HashSet<string> { "Reviewer" },
            true,
            new HashSet<Guid> { projectId });
        var invalid = new WorkflowTransitionRequest(
            projectId,
            VerificationWorkflowState.InReview,
            VerificationWorkflowState.InternallyApproved,
            creator,
            "creator",
            true,
            false,
            false,
            false,
            false,
            "approved",
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"));

        Assert.Throws<InvalidOperationException>(() => VerificationWorkflowService.Transition(invalid));

        var reviewer = creator with
        {
            UserId = "reviewer",
            MateriallyEditedProjectVersionIds = new HashSet<Guid>()
        };
        var valid = invalid with { Actor = reviewer };
        var result = VerificationWorkflowService.Transition(valid);

        Assert.Equal(VerificationWorkflowState.InternallyApproved, result.CurrentState);
    }

    [Fact]
    public void VerificationArchive_IsDeterministicAndSelfVerifiable()
    {
        var metadata = new VerificationArchiveMetadata(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "engine-1",
            "rules-1",
            "pcr-1",
            "gwp-1",
            "units-1",
            new[] { "formula-1" },
            new[] { Guid.NewGuid() },
            "verification-archive-v1",
            new string('c', 64),
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"));
        var files = RequiredArchiveFiles();

        var first = VerificationArchiveBuilder.Build(metadata, files);
        var second = VerificationArchiveBuilder.Build(metadata, files);

        Assert.Equal(first.ArchiveSha256, second.ArchiveSha256);
        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);
        Assert.True(VerificationArchiveBuilder.Verify(first));
        Assert.Contains(first.Files, file => file.Path == "hashes.sha256");
    }

    [Fact]
    public void ProjectComparison_ReportsAddedRemovedChangedAndHotspots()
    {
        var previous = new[]
        {
            new ProjectEntitySnapshot("Activity", "a", new string('a', 64), 10m, "RawMaterial"),
            new ProjectEntitySnapshot("Activity", "b", new string('b', 64), 20m, "Manufacturing")
        };
        var current = new[]
        {
            new ProjectEntitySnapshot("Activity", "a", new string('c', 64), 25m, "RawMaterial"),
            new ProjectEntitySnapshot("Activity", "c", new string('d', 64), 5m, "Use")
        };

        var comparison = ProjectVersionComparisonService.Compare(previous, current);

        Assert.Contains(comparison.Changes, item => item.EntityKey == "a" && item.ChangeType == ProjectChangeType.Changed);
        Assert.Contains(comparison.Changes, item => item.EntityKey == "b" && item.ChangeType == ProjectChangeType.Removed);
        Assert.Contains(comparison.Changes, item => item.EntityKey == "c" && item.ChangeType == ProjectChangeType.Added);
        Assert.Equal(0m, comparison.AbsoluteDelta);
        Assert.Equal("b", comparison.Hotspots[0].EntityKey);
    }

    private static AllocationOutput Output(decimal basis) => new(
        Guid.NewGuid(),
        $"product-{basis}",
        basis,
        "kg",
        false,
        false,
        null,
        string.Empty,
        null,
        Array.Empty<Guid>());

    private static FormulaInputDefinition Input(string key, string unit, bool required = true) =>
        new(key, key, "dimension", unit, required, 0m, null);

    private static FormulaValue Value(string key, decimal value, string unit) =>
        new(key, value, unit, value, unit, "identity-v1");

    private static TransportLeg Leg(
        int sequence,
        TransportMode mode,
        decimal distance,
        decimal cargo,
        decimal factor) => new(
            Guid.NewGuid(),
            sequence,
            sequence == 1 ? "A" : "B",
            sequence == 1 ? "B" : "C",
            "TW",
            mode,
            "class",
            "diesel",
            distance,
            "route evidence",
            cargo,
            10m,
            1m,
            0m,
            false,
            0m,
            string.Empty,
            TransportCalculationMethod.TonneKilometre,
            TransportEmissionBoundary.TankToWheel,
            0m,
            0m,
            string.Empty,
            1m,
            new TransportFactorComponents(Guid.NewGuid(), factor, 0m, "t.km", "kgCO2e"),
            new[] { Guid.NewGuid() });

    private static EvidenceRepositorySnapshot EmptyEvidenceRepository() => new(
        Array.Empty<EvidenceDocument>(),
        Array.Empty<EvidenceDocumentVersion>(),
        Array.Empty<EvidenceLink>(),
        Array.Empty<EvidenceAccessLog>(),
        Array.Empty<EvidenceRetentionLock>());

    private static EvidenceUploadRequest UploadRequest(
        byte[] bytes,
        EvidenceScanResult scan,
        Guid? existingDocumentId,
        string filename) => new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            existingDocumentId,
            "Invoice",
            EvidenceCategory.Invoice,
            null,
            null,
            filename,
            "application/pdf",
            bytes,
            "owner",
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            scan,
            "object-v1",
            false);

    private static IReadOnlyList<VerificationArchiveFile> RequiredArchiveFiles()
    {
        var paths = new[]
        {
            "report/inventory-report.html",
            "workbook/inventory.xlsx",
            "manifest/canonical-manifest.json",
            "calculation/line-items.csv",
            "calculation/stage-summary.csv",
            "register/factors.csv",
            "trace/unit-conversions.csv",
            "trace/allocations.csv",
            "evidence/index.csv",
            "validation/readiness.json",
            "review/findings.json",
            "verification/records.json",
            "audit/events.json"
        };

        return paths.Select(path => new VerificationArchiveFile(
            path,
            Encoding.UTF8.GetBytes(path),
            path.EndsWith(".json", StringComparison.Ordinal) ? "application/json" : "application/octet-stream"))
            .ToArray();
    }
}
