using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Standards;

namespace CarbonFootprint.Unit.Tests;

public sealed class PcrRuleEngineTests
{
    [Fact]
    public void Validate_AcceptsCompatibleCompleteInventory()
    {
        var rules = CreateRuleSet();
        var project = CreateProject();

        var violations = PcrRuleEngine.Validate(rules, project, requireCompleteInventory: true);

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData(PcrPublicationStatus.Draft, PcrReviewStatus.Approved, "PCR-STATUS")]
    [InlineData(PcrPublicationStatus.Published, PcrReviewStatus.Pending, "PCR-STATUS")]
    [InlineData(PcrPublicationStatus.Withdrawn, PcrReviewStatus.Approved, "PCR-STATUS")]
    public void Validate_RejectsUnavailableRuleSet(
        PcrPublicationStatus publicationStatus,
        PcrReviewStatus reviewStatus,
        string expectedCode)
    {
        var rules = CreateRuleSet() with
        {
            PublicationStatus = publicationStatus,
            ReviewStatus = reviewStatus
        };

        var violations = PcrRuleEngine.Validate(rules, CreateProject(), requireCompleteInventory: true);

        Assert.Contains(violations, item => item.Code == expectedCode);
    }

    [Fact]
    public void Validate_RejectsExpiredAndUnapprovedCustomRule()
    {
        var rules = CreateRuleSet() with
        {
            ExpiryDate = new DateOnly(2025, 12, 31),
            IsCustomRule = true,
            CustomRuleJustification = "沒有適用官方 PCR。",
            CustomApprovalStatus = PcrCustomApprovalStatus.Pending
        };

        var violations = PcrRuleEngine.Validate(rules, CreateProject(), requireCompleteInventory: true);

        Assert.Contains(violations, item => item.Code == "PCR-STATUS");
    }

    [Fact]
    public void Validate_RejectsIncompatibleProjectMetadata()
    {
        var project = CreateProject() with
        {
            ProductCategoryCode = "OTHER",
            FunctionalUnit = "100 kg",
            DeclaredUnitCode = "kg",
            SystemBoundaryCode = "cradle-to-gate",
            AllocationMethod = "economic"
        };

        var violations = PcrRuleEngine.Validate(CreateRuleSet(), project, requireCompleteInventory: false);

        Assert.Equal(
            [
                "PCR-PRODUCT-SCOPE",
                "PCR-FUNCTIONAL-UNIT",
                "PCR-DECLARED-UNIT",
                "PCR-SYSTEM-BOUNDARY",
                "PCR-ALLOCATION"
            ],
            violations.Select(item => item.Code));
    }

    [Fact]
    public void Validate_RejectsMissingStageUnsupportedActivityAndRequiredField()
    {
        var project = CreateProject() with
        {
            StageApplicability = new Dictionary<LifecycleStage, bool>
            {
                [LifecycleStage.RawMaterial] = false,
                [LifecycleStage.Manufacturing] = true
            },
            Activities =
            [
                new(
                    Guid.NewGuid(),
                    LifecycleStage.Manufacturing,
                    ActivityDataKind.Material,
                    new HashSet<string>(StringComparer.Ordinal) { "SourceReference" })
            ]
        };

        var violations = PcrRuleEngine.Validate(CreateRuleSet(), project, requireCompleteInventory: true);

        Assert.Contains(violations, item => item.Code == "PCR-STAGE-REQUIRED");
        Assert.Contains(violations, item => item.Code == "PCR-ACTIVITY-TYPE");
        Assert.Contains(violations, item => item.Code == "PCR-ACTIVITY-FIELD");
    }

    [Fact]
    public void Validate_CutoffRuleRequiresExplicitExclusionDisclosure()
    {
        var project = CreateProject() with { Exclusions = string.Empty };

        var violations = PcrRuleEngine.Validate(CreateRuleSet(), project, requireCompleteInventory: true);

        Assert.Contains(violations, item => item.Code == "PCR-CUTOFF-DISCLOSURE");
    }

    private static PcrRuleSetVersion CreateRuleSet() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "PCR-TEST",
        1,
        "ELECTRONICS-*",
        new DateOnly(2025, 12, 1),
        new DateOnly(2026, 1, 1),
        new DateOnly(2027, 12, 31),
        PcrPublicationStatus.Published,
        PcrReviewStatus.Approved,
        "1 piece*",
        "piece",
        "cradle-to-grave",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mass" },
        1m,
        "pcr-test-formulas-v1",
        3,
        "stage totals and product total",
        false,
        string.Empty,
        PcrCustomApprovalStatus.NotRequired,
        null,
        null,
        [
            new(
                LifecycleStage.RawMaterial,
                PcrStageRequirement.Mandatory,
                new HashSet<ActivityDataKind> { ActivityDataKind.Material, ActivityDataKind.MaterialTransport },
                new HashSet<string>(StringComparer.Ordinal) { "SourceReference" }),
            new(
                LifecycleStage.Manufacturing,
                PcrStageRequirement.Mandatory,
                new HashSet<ActivityDataKind> { ActivityDataKind.Energy },
                new HashSet<string>(StringComparer.Ordinal) { "SourceReference", "DataProvider" })
        ]);

    private static PcrProjectContext CreateProject()
    {
        var rawMaterialActivityId = Guid.NewGuid();
        var manufacturingActivityId = Guid.NewGuid();
        return new(
            Guid.NewGuid(),
            "ELECTRONICS-001",
            new DateOnly(2026, 12, 31),
            "1 piece of product",
            "piece",
            "cradle-to-grave",
            "mass",
            new Dictionary<LifecycleStage, bool>
            {
                [LifecycleStage.RawMaterial] = true,
                [LifecycleStage.Manufacturing] = true
            },
            [
                new(
                    rawMaterialActivityId,
                    LifecycleStage.RawMaterial,
                    ActivityDataKind.Material,
                    new HashSet<string>(StringComparer.Ordinal) { "SourceReference" }),
                new(
                    manufacturingActivityId,
                    LifecycleStage.Manufacturing,
                    ActivityDataKind.Energy,
                    new HashSet<string>(StringComparer.Ordinal) { "SourceReference", "DataProvider" })
            ],
            "No inventory items were excluded.");
    }
}
