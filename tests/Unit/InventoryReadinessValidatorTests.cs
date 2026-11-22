using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Standards;

namespace CarbonFootprint.Unit.Tests;

public sealed class InventoryReadinessValidatorTests
{
    private static readonly Guid ProjectId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ActivityId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly string EvidenceHash = new('b', 64);
    private static readonly DateOnly AsOf = new(2026, 10, 1);

    [Fact]
    public void CompleteInventory_HasNoBlockingFindingsAndCanSubmit()
    {
        var report = InventoryReadinessValidator.Validate(Input(Snapshot()));

        Assert.DoesNotContain(report.Findings, item => item.Severity <= ReadinessSeverity.RequiredExplanation);
        Assert.True(report.CanSubmit);
    }

    [Theory]
    [InlineData("RDY-STAGE-ACTIVITY")]
    [InlineData("RDY-STAGE-REASON")]
    [InlineData("RDY-STAGE-DECLARATION")]
    [InlineData("RDY-ACTIVITY-KIND")]
    [InlineData("RDY-ACTIVITY-NEGATIVE")]
    [InlineData("RDY-ACTIVITY-PERIOD")]
    [InlineData("RDY-DATA-QUALITY")]
    [InlineData("RDY-FACTOR-STATUS")]
    [InlineData("RDY-FACTOR-VALIDITY")]
    [InlineData("RDY-FACTOR-UNIT")]
    [InlineData("RDY-ALLOCATION-RANGE")]
    [InlineData("RDY-ALLOCATION-REASON")]
    [InlineData("RDY-ESTIMATE-REASON")]
    [InlineData("RDY-EVIDENCE-MISSING")]
    [InlineData("RDY-EVIDENCE-INTEGRITY")]
    [InlineData("RDY-PERIOD-INVALID")]
    [InlineData("RDY-CALC-STALE")]
    [InlineData("PCR-STATUS")]
    public void EachBlockingCondition_BlocksSubmissionWithItsStableCode(string code)
    {
        var report = InventoryReadinessValidator.Validate(code switch
        {
            "RDY-STAGE-ACTIVITY" => Input(Snapshot(activities: [])),
            "RDY-STAGE-REASON" => Input(Snapshot(stages: Stages(excluded: LifecycleStage.Use, reason: " "))),
            "RDY-STAGE-DECLARATION" => Input(Snapshot(stages: Stages().Take(4).ToArray())),
            "RDY-ACTIVITY-KIND" => Input(Snapshot(Activity(kind: ActivityDataKind.Energy))),
            "RDY-ACTIVITY-NEGATIVE" => Input(Snapshot(Activity(canonicalValue: -1m))),
            "RDY-ACTIVITY-PERIOD" => Input(Snapshot(Activity(periodEnd: new DateOnly(2027, 1, 1)))),
            "RDY-DATA-QUALITY" => Input(Snapshot(Activity(dataQuality: ""))),
            "RDY-FACTOR-STATUS" => Input(Snapshot(Activity(factor: Factor() with { Status = FactorPublicationStatus.Withdrawn }))),
            "RDY-FACTOR-VALIDITY" => Input(Snapshot(Activity(factor: Factor() with { ValidTo = new DateOnly(2026, 3, 1) }))),
            "RDY-FACTOR-UNIT" => Input(Snapshot(Activity(factor: Factor() with { DenominatorUnitCode = "g" }))),
            "RDY-ALLOCATION-RANGE" => Input(Snapshot(Activity(allocation: 1.5m))),
            "RDY-ALLOCATION-REASON" => Input(Snapshot(Activity(allocation: 0.5m)) with { AllocationReason = "" }),
            "RDY-ESTIMATE-REASON" => Input(Snapshot(Activity(estimated: true, estimationReason: ""))),
            "RDY-EVIDENCE-MISSING" => Input(Snapshot(), evidence: []),
            "RDY-EVIDENCE-INTEGRITY" => Input(Snapshot(), evidence: [new(ActivityId, EvidenceHash, "Infected")]),
            "RDY-PERIOD-INVALID" => Input(Snapshot() with { PeriodStart = new DateOnly(2027, 1, 1) }),
            "RDY-CALC-STALE" => Input(Snapshot(), calculation: new("RDY-CALC-STALE", "stale")),
            "PCR-STATUS" => Input(Snapshot(), pcr: [new("PCR-STATUS", "InventoryProjectVersion", ProjectId.ToString(), "unavailable")]),
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        });

        Assert.Contains(report.Findings, item => item.Code == code && item.Severity == ReadinessSeverity.BlockingError);
        Assert.False(report.CanSubmit);
    }

    [Fact]
    public void RequiredExplanation_BlocksUntilAcknowledgedWithJustification()
    {
        var stale = Activity(periodEnd: new DateOnly(2020, 12, 31), periodStart: new DateOnly(2020, 1, 1), factor: Factor() with { ValidFrom = null });
        var snapshot = Snapshot(stale) with { PeriodStart = new DateOnly(2020, 1, 1), PeriodEnd = new DateOnly(2020, 12, 31) };

        var pending = InventoryReadinessValidator.Validate(Input(snapshot));
        var finding = Assert.Single(pending.UnacknowledgedExplanations);
        Assert.Equal("RDY-ACTIVITY-AGE", finding.Code);
        Assert.False(pending.CanSubmit);

        var blank = InventoryReadinessValidator.Validate(Input(snapshot, acknowledgements: [new(finding.Code, finding.EntityKey, "  ")]));
        Assert.False(blank.CanSubmit);

        var acknowledged = InventoryReadinessValidator.Validate(
            Input(snapshot, acknowledgements: [new(finding.Code, finding.EntityKey, "Latest audited meter reading.")]));
        Assert.True(acknowledged.CanSubmit);
        Assert.Contains(acknowledged.Findings, item => item.Code == "RDY-ACTIVITY-AGE");
    }

    [Fact]
    public void HighEstimatedShare_RequiresExplanationOnProjectLevel()
    {
        var report = InventoryReadinessValidator.Validate(Input(Snapshot(Activity(estimated: true, estimationReason: "supplier proxy"))));

        var finding = Assert.Single(report.Findings, item => item.Code == "RDY-ESTIMATE-RATIO");
        Assert.Equal(ReadinessSeverity.RequiredExplanation, finding.Severity);
        Assert.Equal(ProjectId.ToString(), finding.EntityKey);
        Assert.False(report.CanSubmit);
    }

    [Fact]
    public void Report_IsDeterministicAndOrderedBySeverityThenStage()
    {
        var snapshot = Snapshot(Activity(factor: Factor() with { Status = FactorPublicationStatus.Withdrawn }, allocation: 1.5m));
        var first = InventoryReadinessValidator.Validate(Input(snapshot));
        var second = InventoryReadinessValidator.Validate(Input(snapshot));

        Assert.Equal(first.Findings, second.Findings);
        Assert.Equal(first.Sha256(), second.Sha256());
        Assert.Equal(first.Findings.OrderBy(item => item.Severity).Select(item => item.Severity), first.Findings.Select(item => item.Severity));
        Assert.All(first.Findings, item => Assert.False(string.IsNullOrWhiteSpace(item.Remediation)));
    }

    private static InventoryReadinessInput Input(
        InventoryProjectSnapshot snapshot,
        IReadOnlyList<PcrRuleViolation>? pcr = null,
        IReadOnlyList<ReadinessEvidence>? evidence = null,
        ReadinessCalculationIssue? calculation = null,
        IReadOnlyList<ReadinessAcknowledgement>? acknowledgements = null) =>
        new(snapshot, pcr ?? [], evidence ?? [new(ActivityId, EvidenceHash, "Clean")], calculation, AsOf, acknowledgements);

    private static InventoryProjectSnapshot Snapshot(ActivityDataSnapshot? activity = null, IReadOnlyList<ActivityDataSnapshot>? activities = null,
        IReadOnlyList<StageDeclaration>? stages = null) =>
        new(Guid.NewGuid(), ProjectId, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "1 piece", "pcr-v1",
            "rules-v1", "gwp-v1", "units-v1", stages ?? Stages(), activities ?? [activity ?? Activity()],
            "piece", "cradle-to-grave", "mass", "mass allocation", "none", "none", "");

    private static StageDeclaration[] Stages(LifecycleStage? excluded = null, string reason = "") =>
        Enum.GetValues<LifecycleStage>().Select(stage =>
            stage == LifecycleStage.RawMaterial ? new StageDeclaration(stage, true, null)
            : stage == excluded ? new StageDeclaration(stage, false, reason)
            : new StageDeclaration(stage, false, "not part of fixture")).ToArray();

    private static EmissionFactorVersion Factor() =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, "factor", 2m, "kgCO2e", "kg", "TW", new DateOnly(2026, 1, 1), null,
            FactorPublicationStatus.Published, "dataset-v1", "CC0", FactorReviewStatus.Approved, "global");

    private static ActivityDataSnapshot Activity(
        ActivityDataKind kind = ActivityDataKind.Material, decimal canonicalValue = 1m, DateOnly? periodStart = null, DateOnly? periodEnd = null,
        string dataQuality = "primary", EmissionFactorVersion? factor = null, decimal allocation = 1m, bool estimated = false,
        string? estimationReason = null) =>
        new(ActivityId, Guid.NewGuid(), LifecycleStage.RawMaterial, "Steel", canonicalValue, "kg", canonicalValue, "kg", "units-v1",
            periodStart ?? new DateOnly(2026, 1, 1), periodEnd ?? new DateOnly(2026, 12, 31), factor ?? Factor(), EvidenceHash, kind,
            AllocationFactor: allocation, IsEstimated: estimated, EstimationReason: estimationReason, DataQuality: dataQuality,
            DataProvider: "Supplier A");
}
