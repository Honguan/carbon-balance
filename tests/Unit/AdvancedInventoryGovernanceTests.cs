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
    public void ReadinessValidator_BlocksMissingRequiredStage()
    {
        var context = new InventoryReadinessContext(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 31),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            365,
            true,
            true,
            new HashSet<LifecycleStage> { LifecycleStage.RawMaterial, LifecycleStage.Manufacturing },
            [
                new(
                    Guid.NewGuid(),
                    LifecycleStage.RawMaterial,
                    "owner",
                    new DateOnly(2025, 1, 1),
                    new DateOnly(2025, 12, 31),
                    10m,
                    false,
                    string.Empty,
                    false,
                    string.Empty,
                    new(Guid.NewGuid(), true, true, true, true, true),
                    [new(Guid.NewGuid(), true, true, true, true)])
            ],
            [],
            0.25m,
            "No assumptions.",
            "No exclusions.",
            true,
            true,
            true,
            new HashSet<string>());

        var report = InventoryReadinessValidator.Validate(context);

        Assert.Contains(report.Results, item => item.Code == "INV-STAGE-REQUIRED");
        Assert.False(report.CanSubmit(new HashSet<string>()));
    }

    [Fact]
    public void DataQualityAndUncertainty_AreVersionedAndRepeatable()
    {
        var ruleSetId = Guid.NewGuid();
        var rules = new DataQualityRuleSetVersion(
            ruleSetId,
            "dq-v1",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Enum.GetValues<DataQualityDimension>()
                .Select(dimension => new DataQualityCriterion(dimension, 1, 1m, dimension.ToString(), "criterion"))
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
                    dimension.ToString(),
                    "documented",
                    []))
                .ToArray(),
            "complete");
        var score = assessment.CalculateOverallScore(rules);
        var inputs = new[]
        {
            new UncertaintyInput(Guid.NewGuid(), "electricity", 100m, UncertaintyDistribution.Uniform, 90m, 110m, null, 50m, DataSourceCategory.PrimaryMeasured),
            new UncertaintyInput(Guid.NewGuid(), "transport", 100m, UncertaintyDistribution.Triangular, 50m, 150m, null, 20m, DataSourceCategory.Proxy)
        };

        var first = UncertaintyAnalysisService.Analyze(inputs, simulationIterations: 250, seed: 42);
        var second = UncertaintyAnalysisService.Analyze(inputs, simulationIterations: 250, seed: 42);

        Assert.Equal(3m, score);
        Assert.Equal(assessment.CreateCanonicalHash(score), assessment.CreateCanonicalHash(score));
        Assert.Equal(first.LowerResult, second.LowerResult);
        Assert.Equal(first.UpperResult, second.UpperResult);
        Assert.Equal("transport", first.Sensitivities[0].Name);
    }

    [Fact]
    public void AllocationPool_CalculatesOneHundredPercent()
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
            "mass basis",
            [Output(100m), Output(300m)],
            AllocationPoolStatus.Approved,
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            "owner");

        var result = AllocationPoolCalculator.Calculate(pool, DateTimeOffset.Parse("2026-07-31T01:00:00Z"));
        var quantities = result.Shares.Select(item => item.AllocatedResourceQuantity).OrderBy(value => value).ToArray();

        Assert.Equal(1m, result.ShareTotal);
        Assert.Equal([250m, 750m], quantities);
        Assert.Contains("denominator=400", result.CanonicalTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void FormulaRegistry_ExecutesRegisteredPublishedFormula()
    {
        var definition = new ActivityFormulaDefinitionVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "factor-emission-v1",
            ActivityCategory.PurchasedElectricity,
            FormulaCalculationStrategy.FactorBased,
            FormulaPublicationStatus.Published,
            [Input("activityAmount", "kWh"), Input("emissionFactor", "kgCO2e/kWh"), Input("allocationFactor", "ratio", false)],
            "emissions",
            "kgCO2e",
            FactorBasedFormula.ImplementationIdentifier,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "admin",
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        var registry = new ActivityFormulaRegistry([new FactorBasedFormula()], [definition]);
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
        Assert.Contains("implementation=factor-based-v1", result.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void TransportChain_CalculatesTwoLegRoute()
    {
        var chain = new TransportChainVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "route",
            [Leg(1, TransportMode.Road, 100m, 2m, 0.5m), Leg(2, TransportMode.Sea, 1000m, 2m, 0.01m)],
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            "owner",
            false,
            null);

        var result = TransportChainCalculator.Calculate(chain);

        Assert.Equal(2, result.Legs.Count);
        Assert.Equal(120m, result.TotalEmissions);
        Assert.Equal([1, 2], result.Legs.Select(item => item.Sequence).ToArray());
    }

    [Fact]
    public void GlobalFactorSynchronization_IsIdempotent()
    {
        var empty = new GlobalFactorCatalogSnapshot([], [], [], []);
        var source = new[]
        {
            new OfficialFactorSourceRecord(
                "MOENV-001",
                "MOENV",
                "CFP_P_02",
                "Grid electricity",
                0.5m,
                "kgCO2e",
                "kWh",
                "TW",
                "grid",
                null,
                null,
                new DateOnly(2026, 1, 1),
                "2026",
                "open",
                "https://example.test/factor",
                new string('a', 64),
                false)
        };

        var first = GlobalFactorCatalogService.Synchronize(empty, source, new string('b', 64), DateTimeOffset.Parse("2026-07-31T00:00:00Z"));
        var second = GlobalFactorCatalogService.Synchronize(first.Catalog, source, new string('b', 64), DateTimeOffset.Parse("2026-07-31T01:00:00Z"));

        Assert.Single(first.AddedFactorIds);
        Assert.Single(first.AddedVersionIds);
        Assert.Empty(second.AddedFactorIds);
        Assert.Empty(second.AddedVersionIds);
    }

    [Fact]
    public void EvidenceUpload_ComputesHashAndDeduplicatesBytes()
    {
        var repository = new EvidenceRepositorySnapshot([], [], [], [], []);
        var bytes = Encoding.UTF8.GetBytes("invoice-content");
        var scan = new EvidenceScanResult(EvidenceScanStatus.Clean, "ClamAV", "1.5", "daily", DateTimeOffset.Parse("2026-07-31T00:00:00Z"), "clean");

        var first = EvidenceDocumentService.Upload(repository, Upload(bytes, scan, "invoice-a.pdf"));
        var second = EvidenceDocumentService.Upload(first.Repository, Upload(bytes, scan, "invoice-b.pdf"));

        Assert.True(first.Version.Hash.IsVerifiedSha256);
        Assert.False(first.ReusedPhysicalObject);
        Assert.True(second.ReusedPhysicalObject);
        Assert.Equal(first.Version.ObjectStorageKey, second.Version.ObjectStorageKey);
    }

    [Fact]
    public void VerificationWorkflow_EnforcesSeparationOfDuties()
    {
        var projectId = Guid.NewGuid();
        var creator = new WorkflowActor(
            "creator",
            Guid.NewGuid(),
            new HashSet<string> { "Reviewer" },
            true,
            new HashSet<Guid> { projectId });
        var request = new WorkflowTransitionRequest(
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

        Assert.Throws<InvalidOperationException>(() => VerificationWorkflowService.Transition(request));

        var reviewer = creator with
        {
            UserId = "reviewer",
            MateriallyEditedProjectVersionIds = new HashSet<Guid>()
        };
        var result = VerificationWorkflowService.Transition(request with { Actor = reviewer });

        Assert.Equal(VerificationWorkflowState.InternallyApproved, result.CurrentState);
    }

    [Fact]
    public void VerificationArchive_IsDeterministicAndVerifiable()
    {
        var metadata = new VerificationArchiveMetadata(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "engine-1",
            "rules-1",
            "pcr-1",
            "gwp-1",
            "units-1",
            ["formula-1"],
            [Guid.NewGuid()],
            "verification-archive-v1",
            new string('c', 64),
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"));
        var files = RequiredArchiveFiles();

        var first = VerificationArchiveBuilder.Build(metadata, files);
        var second = VerificationArchiveBuilder.Build(metadata, files);

        Assert.Equal(first.ArchiveSha256, second.ArchiveSha256);
        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);
        Assert.True(VerificationArchiveBuilder.Verify(first));
    }

    [Fact]
    public void ProjectComparison_ReportsChangeTypesAndHotspot()
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

        var result = ProjectVersionComparisonService.Compare(previous, current);

        Assert.Contains(result.Changes, item => item.EntityKey == "a" && item.ChangeType == ProjectChangeType.Changed);
        Assert.Contains(result.Changes, item => item.EntityKey == "b" && item.ChangeType == ProjectChangeType.Removed);
        Assert.Contains(result.Changes, item => item.EntityKey == "c" && item.ChangeType == ProjectChangeType.Added);
        Assert.Equal("b", result.Hotspots[0].EntityKey);
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
        []);

    private static FormulaInputDefinition Input(string key, string unit, bool required = true) =>
        new(key, key, "dimension", unit, required, 0m, null);

    private static FormulaValue Value(string key, decimal value, string unit) =>
        new(key, value, unit, value, unit, "identity-v1");

    private static TransportLeg Leg(int sequence, TransportMode mode, decimal distance, decimal cargo, decimal factor) => new(
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
        new(Guid.NewGuid(), factor, 0m, "t.km", "kgCO2e"),
        [Guid.NewGuid()]);

    private static EvidenceUploadRequest Upload(byte[] bytes, EvidenceScanResult scan, string fileName) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        null,
        "Invoice",
        EvidenceCategory.Invoice,
        null,
        null,
        fileName,
        "application/pdf",
        bytes,
        "owner",
        DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
        scan,
        "object-v1",
        false);

    private static IReadOnlyList<VerificationArchiveFile> RequiredArchiveFiles()
    {
        string[] paths =
        [
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
        ];

        return paths
            .Select(path => new VerificationArchiveFile(path, Encoding.UTF8.GetBytes(path), "application/octet-stream"))
            .ToArray();
    }
}
