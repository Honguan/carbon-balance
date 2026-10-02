using CarbonFootprint.Application.Factors;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Infrastructure.LegacyImport;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Organizations;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Web.Services;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using CarbonFootprint.Web.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
using System.Text.Json;
using CarbonFootprint.Web.Security;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace CarbonFootprint.Integration.Tests;

public sealed class PostgreSqlPersistenceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CalculationFreshness_IsBoundedWith100ProjectsAndRecheckedBeforeGovernance()
    {
        var organizationId = Guid.NewGuid();
        await using var context = CreateContext(organizationId);
        var product = new ProductRecord { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Freshness" };
        var productVersion = new ProductVersionRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = product.Id,
            VersionNumber = 1,
            NameZhTw = "Freshness"
        };
        var pcr = CreatePcrVersion(organizationId, PcrPublicationStatus.Published);
        pcr.FormulaRuleSetVersion = ActivityEmissionFormula.PcrFormulaRuleSetV1;
        var factor = CreateFactorVersion(organizationId, Guid.NewGuid(), "Freshness factor", 2m,
            FactorPublicationStatus.Published, FactorReviewStatus.Approved,
            "https://example.test/factor", "factor.csv", new string('a', 64));
        context.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Freshness" });
        context.Products.Add(product);
        context.ProductVersions.Add(productVersion);
        context.PcrVersions.Add(pcr);
        context.EmissionFactorVersions.Add(factor);
        var reviewerId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser { Id = reviewerId, UserName = $"freshness-{reviewerId:N}" });
        var provenance = CalculationBuildProvenance.Create("freshness-test", new string('c', 40));
        var store = new CalculationRunStore(context);
        var projects = new List<InventoryProjectVersionRecord>();
        async Task SeedProjects(int count)
        {
            for (var index = 0; index < count; index++)
            {
                var project = new InventoryProjectVersionRecord
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    ProductVersionId = productVersion.Id,
                    VersionNumber = projects.Count + 1,
                    PeriodStart = new DateOnly(2026, 1, 1),
                    PeriodEnd = new DateOnly(2026, 12, 31),
                    FunctionalUnit = "1 kg product",
                    DeclaredUnit = "kg",
                    SystemBoundary = "cradle-to-grave",
                    PcrVersionId = pcr.Id,
                    PcrVersion = "freshness-pcr-v1",
                    WorkflowStatus = "Draft"
                };
                projects.Add(project);
                context.InventoryProjectVersions.Add(project);
                context.ActivityData.Add(new ActivityDataRecord
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    InventoryProjectVersionId = project.Id,
                    LifecycleStage = (int)LifecycleStage.RawMaterial,
                    Name = "Material",
                    ActivityKind = "Material",
                    RawValue = 1m,
                    RawUnitCode = "kg",
                    CanonicalValue = 1m,
                    CanonicalUnitCode = "kg",
                    AmountFormulaId = ActivityAmountFormula.DirectFormulaId,
                    FormulaInputsJson = "{}",
                    ConversionRuleVersion = "units-p0-v1",
                    PeriodStart = project.PeriodStart,
                    PeriodEnd = project.PeriodEnd,
                    FactorVersionId = factor.Id,
                    AllocationFactor = 1m,
                    EvidenceSha256 = new string('b', 64),
                    DataQuality = "measured"
                });
                context.LifecycleStageDeclarations.AddRange(Enum.GetValues<LifecycleStage>().Select(stage =>
                    new LifecycleStageDeclarationRecord
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = organizationId,
                        InventoryProjectVersionId = project.Id,
                        LifecycleStage = (int)stage,
                        IsApplicable = stage == LifecycleStage.RawMaterial,
                        Reason = stage == LifecycleStage.RawMaterial ? "" : "Test exclusion"
                    }));
                await context.SaveChangesAsync();
                await using var snapshotContext = CreateContext(organizationId);
                await store.SaveAsync(new CalculationEngine().Calculate(Guid.NewGuid(),
                    await new InventorySnapshotReader(snapshotContext).ReadAsync(project.Id, CancellationToken.None), provenance), CancellationToken.None);
            }
        }
        await SeedProjects(1);
        var selected = projects[0];
        async Task<(WorkspaceModel Page, IActionResult Result, int Queries, double Milliseconds)> Request(
            Func<WorkspaceModel, Task<IActionResult>>? action = null, Guid? projectId = null, string section = "calculation")
        {
            var counter = new QueryCounter();
            await using var requestContext = new CarbonFootprintDbContext(
                new DbContextOptionsBuilder<CarbonFootprintDbContext>(CreateOptions()).AddInterceptors(counter).Options,
                new TestOrganizationScope(organizationId));
            using var services = new ServiceCollection().AddSingleton(requestContext).AddLogging()
                .AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<CarbonFootprintDbContext>()
                .Services.BuildServiceProvider();
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, reviewerId.ToString())], "test"))
            };
            var page = new WorkspaceModel(requestContext, new TestOrganizationScope(organizationId), null!, null!, null!,
                services.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, new InventorySnapshotReader(requestContext),
                new AllowAuthorization(), null!, null!, new EphemeralDataProtectionProvider(), provenance)
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new MemoryTempDataProvider()),
                Section = section,
                ProjectVersionId = projectId ?? selected.Id
            };
            var watch = Stopwatch.StartNew();
            var result = await (action?.Invoke(page) ?? page.OnGetAsync(CancellationToken.None));
            watch.Stop();
            Assert.False(requestContext.ChangeTracker.HasChanges());
            return (page, result, counter.Count, watch.Elapsed.TotalMilliseconds);
        }
        await Request(); // Warm EF query compilation before measuring.
        var one = await Request();
        Assert.Contains(selected.Id, one.Page.CurrentRunProjectIds);
        await SeedProjects(99);
        var hundred = await Request();
        output.WriteLine($"Freshness benchmark: 1 project={one.Queries} queries/{one.Milliseconds:F2} ms; 100 projects={hundred.Queries} queries/{hundred.Milliseconds:F2} ms.");
        Assert.Equal(one.Queries, hundred.Queries);
        Assert.InRange(hundred.Queries, 1, 40);
        Assert.Equal(selected.Id, Assert.Single(hundred.Page.CurrentRunProjectIds));
        var switched = await Request(projectId: projects[99].Id);
        Assert.Equal(projects[99].Id, Assert.Single(switched.Page.CurrentRunProjectIds));
        Assert.Empty((await Request(section: "product")).Page.CurrentRunProjectIds);

        var activity = await context.ActivityData.SingleAsync(item => item.InventoryProjectVersionId == selected.Id);
        var stage = await context.LifecycleStageDeclarations.SingleAsync(item =>
            item.InventoryProjectVersionId == selected.Id && item.LifecycleStage == (int)LifecycleStage.Manufacturing);
        var originalRun = await context.CalculationRuns.SingleAsync(item => item.ProjectVersionId == selected.Id);
        var originalManifest = originalRun.CanonicalInputManifest;
        var replacementPcr = CreatePcrVersion(organizationId, PcrPublicationStatus.Published);
        replacementPcr.FormulaRuleSetVersion = "changed-formula-rules-v2";
        context.PcrVersions.Add(replacementPcr);
        await context.SaveChangesAsync();
        var mutations = new (string Name, Action Change, Action Restore)[]
        {
            ("functional unit", () => selected.FunctionalUnit = "2 kg product", () => selected.FunctionalUnit = "1 kg product"),
            ("system boundary", () => selected.SystemBoundary = "cradle-to-gate", () => selected.SystemBoundary = "cradle-to-grave"),
            ("activity quantity", () => activity.CanonicalValue = 2m, () => activity.CanonicalValue = 1m),
            ("raw unit", () => activity.RawUnitCode = "g", () => activity.RawUnitCode = "kg"),
            ("unit catalogue", () => activity.ConversionRuleVersion = "units-p0-v2", () => activity.ConversionRuleVersion = "units-p0-v1"),
            ("allocation", () => activity.AllocationFactor = 0.5m, () => activity.AllocationFactor = 1m),
            ("formula inputs", () => activity.FormulaInputsJson = "{\"amount\":2}", () => activity.FormulaInputsJson = "{}"),
            ("evidence reference", () => activity.EvidenceSha256 = new string('d', 64), () => activity.EvidenceSha256 = new string('b', 64)),
            ("stage declaration", () => stage.Reason = "Changed exclusion", () => stage.Reason = "Test exclusion"),
            ("activity retirement", () => activity.RetiredAt = DateTimeOffset.UtcNow, () => activity.RetiredAt = null),
            ("factor withdrawal", () => factor.PublicationStatus = "Withdrawn", () => factor.PublicationStatus = "Published"),
            ("factor review", () => factor.ReviewStatus = "Rejected", () => factor.ReviewStatus = "Approved"),
            ("factor validity", () => factor.ValidTo = new DateOnly(2026, 6, 30), () => factor.ValidTo = null),
            ("factor value", () => factor.Value = 3m, () => factor.Value = 2m),
            ("PCR withdrawal", () => pcr.PublicationStatus = "Withdrawn", () => pcr.PublicationStatus = "Published"),
            ("PCR deprecation", () => pcr.DeprecatedAt = DateTimeOffset.UtcNow, () => pcr.DeprecatedAt = null),
            ("PCR formula version", () => selected.PcrVersionId = replacementPcr.Id, () => selected.PcrVersionId = pcr.Id)
        };
        foreach (var mutation in mutations)
        {
            mutation.Change();
            await context.SaveChangesAsync();
            Assert.Empty((await Request()).Page.CurrentRunProjectIds);
            var submission = await Request(page => page.OnPostSubmitInventoryAsync(selected.Id, CancellationToken.None));
            Assert.IsType<PageResult>(submission.Result);
            Assert.False(submission.Page.ModelState.IsValid);
            selected.WorkflowStatus = "Submitted";
            await context.SaveChangesAsync();
            var approval = await Request(page => page.OnPostReviewInventoryAsync(selected.Id,
                InventoryWorkflowStatus.Approved, "Reviewed", CancellationToken.None));
            Assert.IsType<PageResult>(approval.Result);
            Assert.False(approval.Page.ModelState.IsValid);
            await context.Entry(selected).ReloadAsync();
            Assert.Equal("Submitted", selected.WorkflowStatus);
            selected.WorkflowStatus = "Draft";
            mutation.Restore();
            await context.SaveChangesAsync();
            Assert.Contains(selected.Id, (await Request()).Page.CurrentRunProjectIds);
            output.WriteLine($"Freshness and governance reject changed {mutation.Name}.");
        }
        // A stale submission must remain returnable for correction, even when approval is blocked.
        selected.WorkflowStatus = "Submitted";
        factor.PublicationStatus = "Withdrawn";
        await context.SaveChangesAsync();
        Assert.IsType<RedirectToPageResult>((await Request(page => page.OnPostReviewInventoryAsync(selected.Id,
            InventoryWorkflowStatus.ChangesRequested, "Replace withdrawn factor", CancellationToken.None))).Result);
        await context.Entry(selected).ReloadAsync();
        Assert.Equal("ChangesRequested", selected.WorkflowStatus);
        factor.PublicationStatus = "Published";
        await context.SaveChangesAsync();
        Assert.IsType<RedirectToPageResult>((await Request(page =>
            page.OnPostSubmitInventoryAsync(selected.Id, CancellationToken.None))).Result);
        Assert.IsType<RedirectToPageResult>((await Request(page => page.OnPostReviewInventoryAsync(selected.Id,
            InventoryWorkflowStatus.Approved, "Reviewed", CancellationToken.None))).Result);
        await context.Entry(selected).ReloadAsync();
        Assert.Equal("Approved", selected.WorkflowStatus);
        await context.Entry(originalRun).ReloadAsync();
        Assert.Equal(originalManifest, originalRun.CanonicalInputManifest);
        Assert.True(CanonicalManifest.HasValidSha256(originalRun.CanonicalInputManifest, originalRun.InputSha256));

        // Corrupt only a synthetic new run, leaving the historical run untouched.
        context.CalculationRuns.Add(new CalculationRunRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProjectVersionId = selected.Id,
            CanonicalInputManifest = originalManifest + " ",
            InputSha256 = originalRun.InputSha256,
            EngineBuild = originalRun.EngineBuild,
            RuleSetVersion = originalRun.RuleSetVersion,
            UnitCatalogueVersion = originalRun.UnitCatalogueVersion,
            GwpVersion = originalRun.GwpVersion,
            PcrVersion = originalRun.PcrVersion,
            DataQualitySummaryJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        selected.WorkflowStatus = "Draft";
        await context.SaveChangesAsync();
        var corrupted = await Request();
        Assert.Empty(corrupted.Page.CurrentRunProjectIds);
        Assert.False(corrupted.Page.LatestManifestHashValid);
        Assert.IsType<PageResult>((await Request(page => page.OnPostSubmitInventoryAsync(selected.Id, CancellationToken.None))).Result);
        selected.WorkflowStatus = "Submitted";
        await context.SaveChangesAsync();
        Assert.IsType<PageResult>((await Request(page => page.OnPostReviewInventoryAsync(selected.Id,
            InventoryWorkflowStatus.Approved, "Reviewed", CancellationToken.None))).Result);
        var export = await Request(page => page.OnGetExportExcelAsync(selected.Id, CancellationToken.None));
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(export.Result).StatusCode);
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Count++;
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return new(result);
        }
    }

    [Fact]
    public async Task InventorySnapshotReader_PreservesProvenanceAndDetectsChangedInputsWithoutWriting()
    {
        var organizationId = Guid.NewGuid();
        await using var context = CreateContext(organizationId);
        var product = new ProductRecord { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Snapshot test" };
        var productVersion = new ProductVersionRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = product.Id,
            VersionNumber = 1,
            NameZhTw = "快照測試"
        };
        var pcr = CreatePcrVersion(organizationId, PcrPublicationStatus.Published);
        pcr.FormulaRuleSetVersion = ActivityEmissionFormula.PcrFormulaRuleSetV1;
        pcr.RoundingDecimalPlaces = 6;
        var project = new InventoryProjectVersionRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductVersionId = productVersion.Id,
            VersionNumber = 1,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            FunctionalUnit = "1 kg product",
            DeclaredUnit = "kg",
            SystemBoundary = "cradle-to-grave",
            PcrVersionId = pcr.Id,
            PcrVersion = "snapshot-pcr-v1",
            WorkflowStatus = "Draft"
        };
        var factor = CreateFactorVersion(organizationId, Guid.NewGuid(), "Snapshot factor", 2.5m,
            FactorPublicationStatus.Published, FactorReviewStatus.Approved,
            "https://example.test/factor", "factor.csv", new string('a', 64));
        var activity = new ActivityDataRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            InventoryProjectVersionId = project.Id,
            LifecycleStage = (int)LifecycleStage.RawMaterial,
            Name = "Material",
            ActivityKind = "Material",
            RawValue = 1.234567m,
            RawUnitCode = "kg",
            CanonicalValue = 1.234567m,
            CanonicalUnitCode = "kg",
            ConversionRuleVersion = "units-p0-v1",
            AmountFormulaId = ActivityAmountFormula.DirectFormulaId,
            FormulaInputsJson = "{}",
            PeriodStart = project.PeriodStart,
            PeriodEnd = project.PeriodEnd,
            FactorVersionId = factor.Id,
            AllocationFactor = 0.5m,
            DataQuality = "measured",
            EvidenceSha256 = new string('b', 64),
            DataSourceType = "meter",
            DataProvider = "Test provider",
            CollectionMethod = "measured",
            SourceReference = "test-source",
            SupplierOrScenario = " ",
            EquipmentCategory = "test-equipment"
        };
        context.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Snapshot test" });
        context.Products.Add(product);
        context.ProductVersions.Add(productVersion);
        context.PcrVersions.Add(pcr);
        context.InventoryProjectVersions.Add(project);
        context.EmissionFactorVersions.Add(factor);
        context.ActivityData.Add(activity);
        context.LifecycleStageDeclarations.AddRange(Enum.GetValues<LifecycleStage>().Reverse().Select(stage =>
            new LifecycleStageDeclarationRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                InventoryProjectVersionId = project.Id,
                LifecycleStage = (int)stage,
                IsApplicable = stage == LifecycleStage.RawMaterial,
                Reason = stage == LifecycleStage.RawMaterial ? " " : "Test exclusion"
            }));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reader = new InventorySnapshotReader(context);
        var snapshot = await reader.ReadAsync(project.Id, CancellationToken.None);
        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Equal(organizationId, snapshot.OrganizationId);
        Assert.Equal(project.ProductVersionId, snapshot.ProductVersionId);
        Assert.Equal(project.PcrVersion, snapshot.PcrVersion);
        Assert.Equal(pcr.FormulaRuleSetVersion, snapshot.RuleSetVersion);
        Assert.Equal(pcr.ReportingRequirements, snapshot.ReportingRequirements);
        Assert.Equal(6, snapshot.RoundingDecimalPlaces);
        Assert.Equal("units-p0-v1", snapshot.UnitCatalogueVersion);
        Assert.Equal(Enum.GetValues<LifecycleStage>(), snapshot.Stages.Select(stage => stage.Stage));
        Assert.Null(snapshot.Stages[0].Reason);
        var input = Assert.Single(snapshot.Activities);
        Assert.Equal(1.234567m, input.CanonicalValue);
        Assert.Equal(0.5m, input.AllocationFactor);
        Assert.Equal(activity.EvidenceSha256, input.EvidenceSha256);
        Assert.Equal(activity.SourceReference, input.SourceReference);
        Assert.Equal(activity.DataProvider, input.DataProvider);
        Assert.Equal(activity.CollectionMethod, input.CollectionMethod);
        Assert.Equal(activity.EquipmentCategory, input.EquipmentCategory);
        Assert.Equal(activity.FormulaInputsJson, input.FormulaInputsJson);
        Assert.Null(input.SupplierOrScenario);
        Assert.Equal(factor.Id, input.FactorVersion.Id);
        Assert.Equal(factor.Value, input.FactorVersion.Value);
        var run = new CalculationEngine().Calculate(Guid.NewGuid(), snapshot,
            CalculationBuildProvenance.Create("snapshot-test", new string('c', 40)));
        Assert.Equal(1.54320875m, run.ProductTotal);
        Assert.True(CanonicalManifest.Matches(await reader.ReadAsync(project.Id, CancellationToken.None),
            run.CanonicalInputManifest, run.InputSha256));

        var changed = await context.ActivityData.SingleAsync(item => item.Id == activity.Id);
        changed.SourceReference = "corrected-source";
        await context.SaveChangesAsync();
        Assert.False(CanonicalManifest.Matches(await reader.ReadAsync(project.Id, CancellationToken.None),
            run.CanonicalInputManifest, run.InputSha256));
        Assert.Equal("test-source", input.SourceReference);
        Assert.Equal(1.54320875m, run.ProductTotal);

        var noEvidenceRun = new CalculationEngine().Calculate(Guid.NewGuid(), snapshot with
        {
            Activities = [input with { EvidenceSha256 = null }]
        }, CalculationBuildProvenance.Create("snapshot-test", new string('c', 40)));
        var store = new CalculationRunStore(context);
        await store.SaveAsync(noEvidenceRun, CancellationToken.None);
        await store.SaveAsync(run, CancellationToken.None);
        var transport = ActivityAmountFormula.Derive(ActivityDataKind.MaterialTransport, null, "",
            12.3456m, 7.890m, null, null, null);
        var use = ActivityAmountFormula.Derive(ActivityDataKind.UseEnergy, null, "kWh",
            null, null, 3.50m, 365.00m, 0.001234560m);
        var precisionSnapshot = snapshot with
        {
            FunctionalUnit = "一台電風扇／運輸與使用情境",
            ReportingRequirements = "保留小數精度與中文來源",
            Stages = snapshot.Stages.Select(stage => stage.Stage == LifecycleStage.Use
                ? stage with { IsApplicable = true, Reason = null } : stage).ToArray(),
            Activities =
            [
                input with
                {
                    Id = Guid.NewGuid(), Name = "原料運輸：甲廠 → 乙廠", Kind = ActivityDataKind.MaterialTransport,
                    RawValue = transport.Value, CanonicalValue = transport.Value,
                    RawUnitCode = transport.UnitCode, CanonicalUnitCode = transport.UnitCode,
                    AmountFormulaId = transport.FormulaId, FormulaInputsJson = JsonSerializer.Serialize(transport.Inputs),
                    FactorVersion = input.FactorVersion with { Id = Guid.NewGuid(), DenominatorUnitCode = transport.UnitCode },
                    SourceReference = "供應商運輸紀錄（測試）"
                },
                input with
                {
                    Id = Guid.NewGuid(), Name = "使用階段耗電", Stage = LifecycleStage.Use, Kind = ActivityDataKind.UseEnergy,
                    RawValue = use.Value, CanonicalValue = use.Value, RawUnitCode = use.UnitCode, CanonicalUnitCode = use.UnitCode,
                    AmountFormulaId = use.FormulaId, FormulaInputsJson = JsonSerializer.Serialize(use.Inputs),
                    FactorVersion = input.FactorVersion with { Id = Guid.NewGuid(), DenominatorUnitCode = use.UnitCode },
                    SourceReference = "測試壽命、頻率與每次消耗"
                }
            ]
        };
        var precisionRun = new CalculationEngine().Calculate(Guid.NewGuid(), precisionSnapshot,
            CalculationBuildProvenance.Create("snapshot-test", new string('c', 40)));
        await store.SaveAsync(precisionRun, CancellationToken.None);
        await using (var persistedContext = CreateContext(organizationId))
        {
            foreach (var saved in new[] { noEvidenceRun, run, precisionRun })
            {
                var persisted = await persistedContext.CalculationRuns.AsNoTracking().SingleAsync(item => item.Id == saved.Id);
                Assert.Equal(saved.CanonicalInputManifest, persisted.CanonicalInputManifest);
                Assert.Equal(saved.InputSha256, persisted.InputSha256);
                Assert.True(CanonicalManifest.HasValidSha256(persisted.CanonicalInputManifest, persisted.InputSha256));
            }
            var persistedPrecision = await persistedContext.CalculationRuns.AsNoTracking().SingleAsync(item => item.Id == precisionRun.Id);
            using var frozen = JsonDocument.Parse(persistedPrecision.CanonicalInputManifest);
            Assert.Equal(precisionSnapshot.FunctionalUnit, frozen.RootElement.GetProperty("functionalUnit").GetString());
            var frozenActivities = frozen.RootElement.GetProperty("activities").EnumerateArray().ToArray();
            var frozenTransport = frozenActivities.Single(activity => activity.GetProperty("amountFormulaId").GetString() == ActivityAmountFormula.TransportFormulaId);
            var frozenUse = frozenActivities.Single(activity => activity.GetProperty("amountFormulaId").GetString() == ActivityAmountFormula.UseScenarioFormulaId);
            Assert.Equal(12.3456m, frozenTransport.GetProperty("formulaInputs").GetProperty("distanceKm").GetDecimal());
            Assert.Equal(7.890m, frozenTransport.GetProperty("formulaInputs").GetProperty("weightKg").GetDecimal());
            Assert.Equal(0.001234560m, frozenUse.GetProperty("formulaInputs").GetProperty("consumptionPerUse").GetDecimal());
            Assert.Equal(transport.Value, frozenTransport.GetProperty("canonicalValue").GetDecimal());
            Assert.Equal(use.Value, frozenUse.GetProperty("canonicalValue").GetDecimal());
        }
        var evidence = new EvidenceFileRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ActivityDataId = activity.Id,
            ObjectKey = "test-evidence",
            OriginalFileName = "=later-evidence.pdf",
            ContentType = "application/pdf",
            Sha256 = new string('b', 64),
            ScanStatus = "Clean",
            SizeBytes = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.EvidenceFiles.Add(evidence);
        await context.SaveChangesAsync();
        var reports = new ReportsModel(context, new AllowAuthorization(), new TestOrganizationScope(organizationId))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };
        var before = Assert.IsType<FileContentResult>(await reports.OnPostEvidenceIndexCsvAsync(noEvidenceRun.Id, CancellationToken.None));
        Assert.DoesNotContain("later-evidence.pdf", Encoding.UTF8.GetString(before.FileContents), StringComparison.Ordinal);
        var csvSnapshot = snapshot with
        {
            FunctionalUnit = "=1+1",
            PcrVersion = "@pcr",
            ReportingRequirements = " \t+SUM(1,2)"
        };
        var csvRun = new CalculationEngine().Calculate(Guid.NewGuid(), csvSnapshot,
            CalculationBuildProvenance.Create("snapshot-test", new string('c', 40)));
        await store.SaveAsync(csvRun, CancellationToken.None);
        var inventoryCsv = Assert.IsType<FileContentResult>(await reports.OnPostInventoryCsvAsync(csvRun.Id, CancellationToken.None));
        var csvText = Encoding.UTF8.GetString(inventoryCsv.FileContents);
        Assert.Contains("\"'=1+1\"", csvText, StringComparison.Ordinal);
        Assert.Contains("\"'@pcr\"", csvText, StringComparison.Ordinal);
        Assert.Contains("\"' \t+SUM(1,2)\"", csvText, StringComparison.Ordinal);
        Assert.Contains("\"1.543209\"", csvText, StringComparison.Ordinal);
        var evidenceCsv = Assert.IsType<FileContentResult>(await reports.OnPostEvidenceIndexCsvAsync(csvRun.Id, CancellationToken.None));
        Assert.Contains("\"'=later-evidence.pdf\"", Encoding.UTF8.GetString(evidenceCsv.FileContents), StringComparison.Ordinal);
        var manifestExport = Assert.IsType<FileContentResult>(await reports.OnPostManifestAsync(csvRun.Id, CancellationToken.None));
        Assert.Equal(Encoding.UTF8.GetBytes(csvRun.CanonicalInputManifest), manifestExport.FileContents);
        Assert.True(CanonicalManifest.HasValidSha256(Encoding.UTF8.GetString(manifestExport.FileContents), csvRun.InputSha256));
        using var identityServices = new ServiceCollection().AddSingleton(context).AddLogging()
            .AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<CarbonFootprintDbContext>()
            .Services.BuildServiceProvider();
        var http = new DefaultHttpContext();
        var permissions = new AllowAuthorization();
        var workspace = new WorkspaceModel(context, new TestOrganizationScope(organizationId), null!, null!, null!,
            identityServices.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, reader,
            permissions, null!, null!, new EphemeralDataProtectionProvider(),
            CalculationBuildProvenance.Create("snapshot-test", new string('c', 40)))
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new MemoryTempDataProvider()),
            Section = "lifecycle",
            Stage = "raw-material",
            ProjectVersionId = project.Id
        };
        workspace.ProjectVersionId = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await workspace.OnGetAsync(CancellationToken.None));
        workspace.ProjectVersionId = project.Id;
        workspace.ActivityId = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await workspace.OnGetAsync(CancellationToken.None));
        workspace.ActivityId = null;
        Task<IActionResult> CorrectActivity(WorkspaceModel page, Guid id) => page.OnPostAddActivityAsync(project.Id, LifecycleStage.RawMaterial,
            ActivityDataKind.Material, "Corrected", "", "supplier", "equipment", "", "source", "",
            "provider", "", "method", "", "corrected-source", 2m, null, null, null, null, null,
            "kg", "kg", factor.Id, 0.5m, false, "", "primary", CancellationToken.None, id);
        permissions.Role = OrganizationRole.Viewer;
        Assert.IsType<ForbidResult>(await CorrectActivity(workspace, activity.Id));
        Assert.IsType<ForbidResult>(await workspace.OnPostRemoveActivityAsync(activity.Id, CancellationToken.None));
        Assert.Single((await reader.ReadAsync(project.Id, CancellationToken.None)).Activities);
        permissions.Role = OrganizationRole.Owner;
        var correction = await CorrectActivity(workspace, activity.Id);
        Assert.IsType<RedirectToPageResult>(correction);
        Assert.NotNull(changed.RetiredAt);
        var corrected = Assert.Single((await reader.ReadAsync(project.Id, CancellationToken.None)).Activities);
        Assert.Equal(2m, corrected.RawValue);
        Assert.NotEqual(activity.Id, corrected.Id);
        Assert.Null(corrected.EvidenceSha256);
        var trackedProject = await context.InventoryProjectVersions.SingleAsync(item => item.Id == project.Id);
        var foreignOrganizationId = Guid.NewGuid();
        await using (var foreignContext = CreateContext(foreignOrganizationId))
        {
            var foreignWorkspace = new WorkspaceModel(foreignContext, new TestOrganizationScope(foreignOrganizationId),
                null!, null!, null!, identityServices.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!,
                new InventorySnapshotReader(foreignContext), new AllowAuthorization(), null!, null!,
                new EphemeralDataProtectionProvider(), CalculationBuildProvenance.Create("snapshot-test", new string('c', 40)))
            {
                PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
            };
            Assert.IsType<NotFoundResult>(await CorrectActivity(foreignWorkspace, corrected.Id));
            Assert.IsType<NotFoundResult>(await foreignWorkspace.OnPostRemoveActivityAsync(corrected.Id, CancellationToken.None));
        }
        foreach (var status in new[] { "Submitted", "Approved" })
        {
            trackedProject.WorkflowStatus = status;
            await context.SaveChangesAsync();
            Assert.IsType<BadRequestResult>(await workspace.OnPostRemoveActivityAsync(corrected.Id, CancellationToken.None));
            Assert.IsType<PageResult>(await CorrectActivity(workspace, corrected.Id));
            Assert.False(workspace.ModelState.IsValid);
            Assert.Equal(corrected.Id, Assert.Single((await reader.ReadAsync(project.Id, CancellationToken.None)).Activities).Id);
            workspace.ModelState.Clear();
        }
        trackedProject.WorkflowStatus = "ChangesRequested";
        await context.SaveChangesAsync();
        Assert.IsType<RedirectToPageResult>(await workspace.OnPostRemoveActivityAsync(corrected.Id, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await workspace.OnPostRemoveActivityAsync(corrected.Id, CancellationToken.None));
        Assert.Empty((await reader.ReadAsync(project.Id, CancellationToken.None)).Activities);
        for (var expectedVersion = 2; expectedVersion <= 3; expectedVersion++)
        {
            Assert.IsType<RedirectToPageResult>(await workspace.OnPostCreateInventoryAsync(productVersion.Id,
                project.PeriodStart, project.PeriodEnd, "next inventory", "kg", "cradle-to-grave",
                "mass", "test allocation", "none", "none", "none", pcr.Id, CancellationToken.None));
            Assert.Equal(expectedVersion, await context.InventoryProjectVersions
                .Where(item => item.ProductVersionId == productVersion.Id).MaxAsync(item => item.VersionNumber));
        }
        var after = Assert.IsType<FileContentResult>(await reports.OnPostEvidenceIndexCsvAsync(run.Id, CancellationToken.None));
        Assert.Contains("later-evidence.pdf", Encoding.UTF8.GetString(after.FileContents), StringComparison.Ordinal);
        Assert.Equal(run.CanonicalInputManifest, (await context.CalculationRuns.SingleAsync(item => item.Id == run.Id)).CanonicalInputManifest);
        Assert.True(await context.EvidenceFiles.AnyAsync(item => item.Id == evidence.Id));
        await reports.OnGetAsync(CancellationToken.None);
        Assert.Equal(6, reports.PcrRulesByRunId[run.Id].RoundingDecimalPlaces);
        var excel = Assert.IsType<FileContentResult>(await workspace.OnGetExportExcelAsync(project.Id, CancellationToken.None, run.Id));
        using (var archive = new ZipArchive(new MemoryStream(excel.FileContents)))
        {
            using var sheet = new StreamReader(archive.GetEntry("xl/worksheets/sheet2.xml")!.Open());
            var xml = await sheet.ReadToEndAsync();
            Assert.Contains("Material", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("Corrected", xml, StringComparison.Ordinal);
            Assert.Contains("test-source", xml, StringComparison.Ordinal);
            Assert.NotNull(archive.GetEntry("xl/worksheets/sheet5.xml"));
        }

        var archiveReport = new ArchiveReportModel(context, new AllowAuthorization())
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };
        try
        {
            var unknownSchema = precisionRun.CanonicalInputManifest.Replace(
                CalculationBuildProvenance.CurrentManifestSchemaVersion, "unknown-schema", StringComparison.Ordinal);
            foreach (var (invalidManifest, invalidHash) in new[]
            {
                (precisionRun.CanonicalInputManifest, new string('0', 64)),
                ("{}", CanonicalManifest.ComputeSha256("{}")),
                (unknownSchema, CanonicalManifest.ComputeSha256(unknownSchema))
            })
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.calculation_runs SET canonical_input_manifest = {invalidManifest}, input_sha256 = {invalidHash} WHERE id = {precisionRun.Id}");
                context.ChangeTracker.Clear();
                var auditCount = await context.AuditEvents.CountAsync();
                await reports.OnGetAsync(CancellationToken.None);
                Assert.Contains(precisionRun.Id, reports.InvalidRunIds);
                Assert.DoesNotContain(precisionRun.Id, reports.PcrRulesByRunId.Keys);
                Assert.Contains(run.Id, reports.PcrRulesByRunId.Keys);
                Assert.Contains(reports.Runs, item => item.Id == precisionRun.Id);
                Func<Task<IActionResult>>[] exports =
                [
                    () => reports.OnPostInventoryCsvAsync(precisionRun.Id, CancellationToken.None),
                    () => reports.OnPostEvidenceIndexCsvAsync(precisionRun.Id, CancellationToken.None),
                    () => reports.OnPostManifestAsync(precisionRun.Id, CancellationToken.None),
                    () => archiveReport.OnGetAsync(precisionRun.Id, CancellationToken.None),
                    () => workspace.OnGetExportExcelAsync(project.Id, CancellationToken.None, precisionRun.Id)
                ];
                foreach (var export in exports)
                {
                    var conflict = Assert.IsType<ObjectResult>(await export());
                    Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
                    Assert.Contains("暫停匯出", Assert.IsType<string>(conflict.Value), StringComparison.Ordinal);
                }
                Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
                Assert.Equal(invalidManifest, (await context.CalculationRuns.AsNoTracking()
                    .SingleAsync(item => item.Id == precisionRun.Id)).CanonicalInputManifest);
            }
        }
        finally
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.calculation_runs SET canonical_input_manifest = {precisionRun.CanonicalInputManifest}, input_sha256 = {precisionRun.InputSha256} WHERE id = {precisionRun.Id}");
            context.ChangeTracker.Clear();
        }

        await using var otherOrganization = CreateContext(Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventorySnapshotReader(otherOrganization).ReadAsync(project.Id, CancellationToken.None));
    }

    [Fact]
    public void Model_HasNoPendingMigrationChanges()
    {
        using var dbContext = CreateContext(Guid.NewGuid());

        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task UnitCatalogueV2_SeedsTransportAndTonneConversions()
    {
        await using var dbContext = CreateContext(Guid.NewGuid());

        var units = await dbContext.Units
            .Where(item => item.CatalogueVersion == "units-p0-v2")
            .OrderBy(item => item.Code)
            .ToArrayAsync();

        Assert.Equal(6, units.Length);
        Assert.Equal(1000m, units.Single(item => item.Code == "tonne").ScaleToCanonical);
        Assert.Equal("transport-work", units.Single(item => item.Code == "tonne-km").Dimension);
        Assert.Equal("count", units.Single(item => item.Code == "piece").Dimension);
    }

    [Fact]
    public async Task QueryFilters_KeepOrganizationsIsolated_AndRejectCrossTenantWrite()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using (var contextA = CreateContext(organizationA))
        {
            contextA.Organizations.Add(new OrganizationRecord
            {
                Id = organizationA,
                Name = "整合測試組織 A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            contextA.Products.Add(new ProductRecord
            {
                Id = productA,
                OrganizationId = organizationA,
                Name = "產品 A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await contextA.SaveChangesAsync();
        }

        await using (var contextB = CreateContext(organizationB))
        {
            contextB.Organizations.Add(new OrganizationRecord
            {
                Id = organizationB,
                Name = "整合測試組織 B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            contextB.Products.Add(new ProductRecord
            {
                Id = productB,
                OrganizationId = organizationB,
                Name = "產品 B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await contextB.SaveChangesAsync();
        }

        await using (var contextA = CreateContext(organizationA))
        {
            Assert.Equal([productA], await contextA.Products.Select(item => item.Id).ToArrayAsync());
            Assert.Null(await contextA.Products.SingleOrDefaultAsync(item => item.Id == productB));

            contextA.Products.Add(new ProductRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                Name = "越權寫入",
                CreatedAt = DateTimeOffset.UtcNow
            });
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => contextA.SaveChangesAsync());
            Assert.Contains("不符合目前組織範圍", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OrganizationMailSettings_AreTenantScoped()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();

        var settingsAId = Guid.NewGuid();
        var settingsBId = Guid.NewGuid();
        await using (var context = CreateContext(organizationA))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationA,
                Name = "SMTP 測試組織 A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationB))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationB,
                Name = "SMTP 測試組織 B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationA))
        {
            context.OrganizationMailSettings.Add(new OrganizationMailSettingsRecord
            {
                Id = settingsAId,
                OrganizationId = organizationA,
                Host = "smtp-a.example.test",
                Port = 587,
                EnableSsl = true,
                FromAddress = "a@example.test",
                FromName = "組織 A",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationB))
        {
            context.OrganizationMailSettings.Add(new OrganizationMailSettingsRecord
            {
                Id = settingsBId,
                OrganizationId = organizationB,
                Host = "smtp-b.example.test",
                Port = 465,
                EnableSsl = true,
                FromAddress = "b@example.test",
                FromName = "組織 B",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationA))
        {
            var settings = await context.OrganizationMailSettings.SingleAsync();
            Assert.Equal("smtp-a.example.test", settings.Host);
            Assert.Empty(settings.EncryptedPassword);
            Assert.Null(await context.OrganizationMailSettings
                .SingleOrDefaultAsync(item => item.Id == settingsBId));

            context.OrganizationMailSettings.Add(new OrganizationMailSettingsRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                Host = "smtp-id-or.example.test",
                Port = 25,
                FromAddress = "id-or@example.test",
                FromName = "越權"
            });
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("不符合目前組織範圍", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task QueryFilters_ResolveOrganizationWhenRequestScopeBecomesAvailable()
    {
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await using (var seededContext = CreateContext(organizationId))
        {
            seededContext.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "延遲租戶測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            seededContext.Products.Add(new ProductRecord
            {
                Id = productId,
                OrganizationId = organizationId,
                Name = "延遲租戶測試產品",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await seededContext.SaveChangesAsync();
        }

        var scope = new MutableOrganizationScope();
        await using var context = CreateContext(scope);
        Assert.Empty(await context.Products.ToArrayAsync());

        scope.OrganizationId = organizationId;
        Assert.Equal([productId], await context.Products.Select(item => item.Id).ToArrayAsync());
    }

    [Fact]
    public async Task LegacyFactorImporter_StagesValidInvalidAndConflictRowsWithoutPublishing()
    {
        var organizationId = Guid.NewGuid();
        var uniqueName = Guid.NewGuid().ToString("N");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"legacy-factors-{uniqueName}.csv");
        await File.WriteAllTextAsync(
            sourcePath,
            $"name,value,denominator_unit,source_version,license_code\n" +
            $"factor-{uniqueName},2.5,kg,dataset-1,fixture\n" +
            $"invalid-{uniqueName},-1,unknown,dataset-1,fixture\n" +
            $"factor-{uniqueName},2.5,kg,dataset-1,fixture\n");

        try
        {
            await using var context = CreateContext(organizationId);
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "Legacy staging 測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();

            var report = await new LegacyFactorCsvImporter(context).ImportAsync(
                organizationId,
                sourcePath,
                CancellationToken.None);

            Assert.Equal(1, report.ParsedRows);
            Assert.Equal(1, report.InvalidRows);
            Assert.Equal(1, report.ConflictRows);
            Assert.Equal(3, await context.LegacyStagingRows.CountAsync());
            Assert.Empty(await context.EmissionFactorVersions.ToArrayAsync());
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task MoenvFactorSynchronization_PublishesOfficialFactorsWithoutReview()
    {
        var organizationId = Guid.NewGuid();
        await using (var context = CreateContext(organizationId))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "部署係數匯入測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                FactorId = Guid.NewGuid(),
                VersionNumber = 1,
                Name = "測試天然氣",
                Value = 2.5m,
                NumeratorUnitCode = "kgCO2e",
                DenominatorUnitCode = "kg",
                Geography = "TW",
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = null,
                PublicationStatus = FactorPublicationStatus.Draft.ToString(),
                SourceDatasetVersion = "CFP_P_02-2026",
                LicenseCode = "政府資料開放授權條款第1版",
                SourceType = "government-database",
                SourceName = "環境部",
                SourceReference = MoenvFactorClient.DatasetReference,
                DatasetName = "環境部碳足跡排放係數",
                OriginalDocumentName = "CFP_P_02-record-aaaaaaaaaaaa.json",
                OriginalDocumentSha256 = new string('a', 64),
                Applicability = "測試適用性",
                ReviewStatus = FactorReviewStatus.Pending.ToString()
            });
            await context.SaveChangesAsync();
        }

        var source = new StubMoenvFactorSource(new MoenvFactorDownload(
            [
                new MoenvFactorRecord(
                    "測試電力",
                    0.5m,
                    "kWh",
                    "環境部",
                    2026,
                    new string('a', 64)),
                new MoenvFactorRecord(
                    "測試天然氣",
                    2.5m,
                    "kg",
                    "環境部",
                    2026,
                    new string('a', 64))
            ],
            2));
        var service = new MoenvFactorSynchronizationService(CreateOptions(), source);

        var first = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "deployment-test",
            CancellationToken.None);
        var second = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "deployment-test",
            CancellationToken.None);

        Assert.Equal(1, first.CreatedCount);
        Assert.Equal(0, first.UnchangedCount);
        Assert.Equal(1, first.PublishedExistingCount);
        Assert.Equal(2, first.SkippedCount);
        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(2, second.UnchangedCount);
        Assert.Equal(0, second.PublishedExistingCount);

        await using var verification = CreateContext(organizationId);
        var factors = await verification.EmissionFactorVersions
            .Where(item => item.SourceReference == MoenvFactorClient.DatasetReference)
            .OrderBy(item => item.Name)
            .ToArrayAsync();
        Assert.Equal(2, factors.Length);
        Assert.All(factors, factor =>
        {
            Assert.Equal(FactorPublicationStatus.Published.ToString(), factor.PublicationStatus);
            Assert.Equal(FactorReviewStatus.NotRequired.ToString(), factor.ReviewStatus);
            Assert.Null(factor.ReviewedAt);
            Assert.NotNull(factor.PublishedAt);
        });
        var factor = factors.Single(item => item.Name == "測試電力");
        var audit = await verification.AuditEvents.SingleAsync(
            item => item.Action == "factor.version.synced" && item.ResourceId == factor.Id);
        Assert.Null(audit.ActorId);
        Assert.Equal("deployment-test", audit.CorrelationId);
        Assert.Single(await verification.AuditEvents
            .Where(item => item.Action == "factor.version.auto-published")
            .ToArrayAsync());
        var synchronizationAudits = await verification.AuditEvents
            .Where(item =>
                item.Action == "factor.synchronization.completed"
                && item.ResourceId == organizationId)
            .OrderBy(item => item.Timestamp)
            .ToArrayAsync();
        Assert.Equal(2, synchronizationAudits.Length);
        Assert.All(synchronizationAudits, item =>
        {
            Assert.Null(item.ActorId);
            Assert.Equal("deployment-test", item.CorrelationId);
            Assert.Contains(MoenvFactorClient.DatasetReference, item.MetadataJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task MoenvFactorSynchronization_DoesNotAutoPublishManualOrWithdrawnVersions()
    {
        var organizationId = Guid.NewGuid();
        var manualFactorId = Guid.NewGuid();
        var withdrawnFactorId = Guid.NewGuid();
        await using (var context = CreateContext(organizationId))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "同步來源辨識測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.EmissionFactorVersions.AddRange(
                CreateFactorVersion(
                    organizationId,
                    manualFactorId,
                    "手動來源係數",
                    1.5m,
                    FactorPublicationStatus.Draft,
                    FactorReviewStatus.Pending,
                    MoenvFactorClient.DatasetReference,
                    "manual-source.pdf",
                    new string('c', 64)),
                CreateFactorVersion(
                    organizationId,
                    withdrawnFactorId,
                    "已撤回官方係數",
                    3.5m,
                    FactorPublicationStatus.Withdrawn,
                    FactorReviewStatus.NotRequired,
                    MoenvFactorClient.DatasetReference,
                    "CFP_P_02-record-dddddddddddd.json",
                    new string('d', 64)));
            await context.SaveChangesAsync();
        }

        var service = new MoenvFactorSynchronizationService(
            CreateOptions(),
            new StubMoenvFactorSource(new MoenvFactorDownload(
                [
                    new MoenvFactorRecord(
                        "手動來源係數",
                        1.5m,
                        "kg",
                        "環境部",
                        2026,
                        new string('c', 64)),
                    new MoenvFactorRecord(
                        "已撤回官方係數",
                        3.5m,
                        "kg",
                        "環境部",
                        2026,
                        new string('d', 64))
                ],
                0)));

        var result = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "source-classification-test",
            CancellationToken.None);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(0, result.PublishedExistingCount);
        await using var verification = CreateContext(organizationId);
        var manual = await verification.EmissionFactorVersions.SingleAsync(item => item.FactorId == manualFactorId);
        Assert.Equal(FactorPublicationStatus.Draft.ToString(), manual.PublicationStatus);
        Assert.Equal(FactorReviewStatus.Pending.ToString(), manual.ReviewStatus);
        var synchronized = await verification.EmissionFactorVersions.SingleAsync(item =>
            item.Name == "手動來源係數" && item.FactorId != manualFactorId);
        Assert.Equal(FactorPublicationStatus.Published.ToString(), synchronized.PublicationStatus);
        Assert.Equal(FactorReviewStatus.NotRequired.ToString(), synchronized.ReviewStatus);
        var withdrawn = await verification.EmissionFactorVersions.SingleAsync(item => item.FactorId == withdrawnFactorId);
        Assert.Equal(FactorPublicationStatus.Withdrawn.ToString(), withdrawn.PublicationStatus);
    }

    [Fact]
    public async Task MoenvFactorSynchronization_UsesNextVersionAcrossManualAndSynchronizedSources()
    {
        var organizationId = Guid.NewGuid();
        var factorId = Guid.NewGuid();
        var synchronizedVersionId = Guid.NewGuid();
        var manualVersionId = Guid.NewGuid();
        await using (var context = CreateContext(organizationId))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "同步跨來源版號測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            var synchronizedVersion = CreateFactorVersion(
                organizationId,
                factorId,
                "跨來源版號係數",
                1m,
                FactorPublicationStatus.Withdrawn,
                FactorReviewStatus.NotRequired,
                MoenvFactorClient.DatasetReference,
                "CFP_P_02-record-eeeeeeeeeeee.json",
                new string('e', 64));
            synchronizedVersion.Id = synchronizedVersionId;
            synchronizedVersion.SourceDatasetVersion = "CFP_P_02-2025";
            var manualVersion = CreateFactorVersion(
                organizationId,
                factorId,
                "跨來源版號係數",
                1.1m,
                FactorPublicationStatus.Published,
                FactorReviewStatus.Approved,
                "manual-reference",
                "manual-update.pdf",
                string.Empty);
            manualVersion.Id = manualVersionId;
            manualVersion.VersionNumber = 2;
            manualVersion.SupersedesVersionId = synchronizedVersionId;
            context.EmissionFactorVersions.AddRange(synchronizedVersion, manualVersion);
            await context.SaveChangesAsync();
        }

        var service = new MoenvFactorSynchronizationService(
            CreateOptions(),
            new StubMoenvFactorSource(new MoenvFactorDownload(
                [
                    new MoenvFactorRecord(
                        "跨來源版號係數",
                        1.2m,
                        "kg",
                        "環境部",
                        2026,
                        new string('f', 64))
                ],
                0)));

        var result = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "cross-source-version-test",
            CancellationToken.None);

        Assert.Equal(1, result.CreatedCount);
        await using var verification = CreateContext(organizationId);
        var versions = await verification.EmissionFactorVersions
            .Where(item => item.FactorId == factorId)
            .OrderBy(item => item.VersionNumber)
            .ToArrayAsync();
        Assert.Equal([1, 2, 3], versions.Select(item => item.VersionNumber));
        Assert.Equal(FactorPublicationStatus.Withdrawn.ToString(), versions[1].PublicationStatus);
        Assert.Equal(FactorPublicationStatus.Published.ToString(), versions[2].PublicationStatus);
        Assert.Equal(FactorReviewStatus.NotRequired.ToString(), versions[2].ReviewStatus);
        Assert.Equal(manualVersionId, versions[2].SupersedesVersionId);
    }

    [Fact]
    public async Task OrganizationInvitation_RequiresMatchingEmail_AndCreatesScopedMembership()
    {
        var organizationId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var inviteeEmail = $"invitee-{inviteeId:N}@example.test";
        await using (var context = CreateContext(organizationId))
        {
            context.Users.AddRange(
                new ApplicationUser
                {
                    Id = ownerId,
                    UserName = $"owner-{ownerId:N}@example.test",
                    NormalizedUserName = $"OWNER-{ownerId:N}@EXAMPLE.TEST"
                },
                new ApplicationUser
                {
                    Id = inviteeId,
                    UserName = inviteeEmail,
                    NormalizedUserName = inviteeEmail.ToUpperInvariant(),
                    Email = inviteeEmail,
                    NormalizedEmail = inviteeEmail.ToUpperInvariant()
                });
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "Invitation integration organization",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var service = new OrganizationInvitationService(CreateOptions());
        var token = await service.CreateAsync(
            organizationId,
            ownerId,
            inviteeEmail,
            OrganizationRole.Contributor,
            CancellationToken.None);
        var wrongUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "wrong@example.test",
            NormalizedEmail = "WRONG@EXAMPLE.TEST"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptAsync(wrongUser, token, CancellationToken.None));

        var invitee = new ApplicationUser
        {
            Id = inviteeId,
            Email = inviteeEmail,
            NormalizedEmail = inviteeEmail.ToUpperInvariant()
        };
        Assert.Equal(organizationId, await service.AcceptAsync(invitee, token, CancellationToken.None));

        await using var verification = CreateContext(organizationId);
        var membership = await verification.OrganizationMemberships.SingleAsync(item => item.UserId == inviteeId);
        Assert.Equal(OrganizationRole.Contributor.ToString(), membership.Role);
        Assert.NotNull((await verification.OrganizationInvitations.SingleAsync()).AcceptedAt);
        Assert.False(await verification.UserClaims.AnyAsync(item =>
            item.UserId == inviteeId && item.ClaimType == OrganizationClaimsTransformation.OrganizationClaimType));
    }

    [Fact]
    public async Task OrganizationClaimsTransformation_RebuildsCurrentMembershipWithoutDuplicates()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = CreateContext(organizationId);
        context.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"claims-{userId:N}@example.test",
            NormalizedUserName = $"CLAIMS-{userId:N}@EXAMPLE.TEST"
        });
        context.Organizations.Add(new OrganizationRecord
        {
            Id = organizationId,
            Name = "Claims transformation organization",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var membership = new OrganizationMembershipRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            Role = OrganizationRole.Owner.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.OrganizationMemberships.Add(membership);
        await context.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(OrganizationClaimsTransformation.OrganizationClaimType, Guid.NewGuid().ToString())
        ], "test"));
        var transformation = new OrganizationClaimsTransformation(context);

        await transformation.TransformAsync(principal);
        await transformation.TransformAsync(principal);

        Assert.Equal(
            organizationId.ToString(),
            Assert.Single(principal.FindAll(OrganizationClaimsTransformation.OrganizationClaimType)).Value);

        membership.RevokedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();
        await transformation.TransformAsync(principal);

        Assert.Empty(principal.FindAll(OrganizationClaimsTransformation.OrganizationClaimType));
    }

    [Fact]
    public async Task ConcurrentInvitations_CreateExactlyOneActiveOrganizationContext()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var inviteeEmail = $"concurrent-{inviteeId:N}@example.test";
        await using (var context = CreateContext(organizationA))
        {
            context.Users.AddRange(
                new ApplicationUser { Id = ownerA, UserName = $"owner-{ownerA:N}", NormalizedUserName = $"OWNER-{ownerA:N}" },
                new ApplicationUser
                {
                    Id = inviteeId,
                    UserName = inviteeEmail,
                    NormalizedUserName = inviteeEmail.ToUpperInvariant(),
                    Email = inviteeEmail,
                    NormalizedEmail = inviteeEmail.ToUpperInvariant()
                });
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationA,
                Name = "Concurrent invitation A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }
        await using (var context = CreateContext(organizationB))
        {
            context.Users.Add(new ApplicationUser
            {
                Id = ownerB,
                UserName = $"owner-{ownerB:N}",
                NormalizedUserName = $"OWNER-{ownerB:N}"
            });
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationB,
                Name = "Concurrent invitation B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var service = new OrganizationInvitationService(CreateOptions());
        var tokenA = await service.CreateAsync(
            organizationA, ownerA, inviteeEmail, OrganizationRole.Contributor, CancellationToken.None);
        var tokenB = await service.CreateAsync(
            organizationB, ownerB, inviteeEmail, OrganizationRole.Reviewer, CancellationToken.None);
        var invitee = new ApplicationUser
        {
            Id = inviteeId,
            Email = inviteeEmail,
            NormalizedEmail = inviteeEmail.ToUpperInvariant()
        };

        var results = await Task.WhenAll(new[] { tokenA, tokenB }.Select(async token =>
        {
            try
            {
                return (Succeeded: true, OrganizationId: await service.AcceptAsync(invitee, token, CancellationToken.None));
            }
            catch (InvalidOperationException)
            {
                return (Succeeded: false, OrganizationId: Guid.Empty);
            }
        }));

        Assert.Single(results, item => item.Succeeded);
        await using var verification = CreateContext(organizationA);
        Assert.Single(await verification.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(item => item.UserId == inviteeId && item.RevokedAt == null)
            .ToArrayAsync());
        Assert.Single(await verification.OrganizationInvitations
            .IgnoreQueryFilters()
            .Where(item => (item.OrganizationId == organizationA || item.OrganizationId == organizationB)
                && item.AcceptedAt != null)
            .ToArrayAsync());

        var sameTokenInviteeId = Guid.NewGuid();
        var sameTokenEmail = $"same-token-{sameTokenInviteeId:N}@example.test";
        await using (var context = CreateContext(organizationA))
        {
            context.Users.Add(new ApplicationUser
            {
                Id = sameTokenInviteeId,
                UserName = sameTokenEmail,
                NormalizedUserName = sameTokenEmail.ToUpperInvariant(),
                Email = sameTokenEmail,
                NormalizedEmail = sameTokenEmail.ToUpperInvariant()
            });
            await context.SaveChangesAsync();
        }
        var sameToken = await service.CreateAsync(
            organizationA, ownerA, sameTokenEmail, OrganizationRole.Contributor, CancellationToken.None);
        var sameTokenInvitee = new ApplicationUser
        {
            Id = sameTokenInviteeId,
            Email = sameTokenEmail,
            NormalizedEmail = sameTokenEmail.ToUpperInvariant()
        };
        var sameTokenResults = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await service.AcceptAsync(sameTokenInvitee, sameToken, CancellationToken.None);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }));
        Assert.Single(sameTokenResults, succeeded => succeeded);
    }

    [Fact]
    public async Task ConcurrentOnboarding_CreatesExactlyOneActiveOrganizationContext()
    {
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"onboarding-{userId:N}@example.test",
            NormalizedUserName = $"ONBOARDING-{userId:N}@EXAMPLE.TEST"
        };
        await using (var context = CreateContext(Guid.NewGuid()))
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var service = new OrganizationOnboardingService(CreateOptions());
        var organizationNames = new[]
        {
            $"Concurrent onboarding A {userId:N}",
            $"Concurrent onboarding B {userId:N}"
        };
        var results = await Task.WhenAll(organizationNames.Select(async name =>
        {
            try
            {
                return (Succeeded: true, OrganizationId: await service.CreateAsync(user, name, CancellationToken.None));
            }
            catch (InvalidOperationException)
            {
                return (Succeeded: false, OrganizationId: Guid.Empty);
            }
        }));

        var success = Assert.Single(results, item => item.Succeeded);
        await using var verification = CreateContext(success.OrganizationId);
        Assert.Single(await verification.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(item => item.UserId == userId && item.RevokedAt == null)
            .ToArrayAsync());
        Assert.Single(await verification.Organizations
            .IgnoreQueryFilters()
            .Where(item => organizationNames.Contains(item.Name))
            .ToArrayAsync());
    }

    [Fact]
    public async Task PublishedPcr_ContentAndStageRulesAreImmutable()
    {
        var organizationId = Guid.NewGuid();
        var pcr = CreatePcrVersion(organizationId, PcrPublicationStatus.Published);
        var stageRule = new PcrStageRuleRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PcrVersionId = pcr.Id,
            LifecycleStage = 1,
            Requirement = PcrStageRequirement.Mandatory.ToString(),
            PermittedActivityKindsCsv = "Material,MaterialTransport",
            RequiredFieldsCsv = "SourceReference"
        };

        await using (var setup = CreateContext(organizationId))
        {
            setup.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "PCR immutability organization",
                CreatedAt = DateTimeOffset.UtcNow
            });
            setup.PcrVersions.Add(pcr);
            setup.PcrStageRules.Add(stageRule);
            await setup.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationId))
        {
            var published = await context.PcrVersions.SingleAsync(item => item.Id == pcr.Id);
            published.Title = "不可覆寫的名稱";
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("請建立新版本", exception.Message, StringComparison.Ordinal);
        }

        await using (var context = CreateContext(organizationId))
        {
            var rule = await context.PcrStageRules.SingleAsync(item => item.Id == stageRule.Id);
            rule.Requirement = PcrStageRequirement.Optional.ToString();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("階段規則不可修改", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PcrStageRules_AreTenantScoped()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var pcrA = CreatePcrVersion(organizationA, PcrPublicationStatus.Draft);
        var pcrB = CreatePcrVersion(organizationB, PcrPublicationStatus.Draft);

        await using (var context = CreateContext(organizationA))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationA,
                Name = "PCR tenant A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.PcrVersions.Add(pcrA);
            context.PcrStageRules.Add(new PcrStageRuleRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationA,
                PcrVersionId = pcrA.Id,
                LifecycleStage = 1,
                Requirement = PcrStageRequirement.Optional.ToString()
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationB))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationB,
                Name = "PCR tenant B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.PcrVersions.Add(pcrB);
            context.PcrStageRules.Add(new PcrStageRuleRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                PcrVersionId = pcrB.Id,
                LifecycleStage = 1,
                Requirement = PcrStageRequirement.Optional.ToString()
            });
            await context.SaveChangesAsync();
        }

        await using var verification = CreateContext(organizationA);
        Assert.Equal([pcrA.Id], await verification.PcrStageRules.Select(item => item.PcrVersionId).ToArrayAsync());
        Assert.Null(await verification.PcrStageRules.SingleOrDefaultAsync(item => item.PcrVersionId == pcrB.Id));
    }

    private static CarbonFootprintDbContext CreateContext(Guid organizationId)
        => CreateContext(new TestOrganizationScope(organizationId));

    private static CarbonFootprintDbContext CreateContext(IOrganizationScope organizationScope)
    {
        return new CarbonFootprintDbContext(CreateOptions(), organizationScope);
    }

    private static DbContextOptions<CarbonFootprintDbContext> CreateOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Integration test 需要 CARBON_TEST_DB_CONNECTION。");
        return new DbContextOptionsBuilder<CarbonFootprintDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
    }

    private sealed record TestOrganizationScope(Guid Value) : IOrganizationScope
    {
        public Guid? OrganizationId => Value;
    }

    private sealed class AllowAuthorization : IAuthorizationService
    {
        public OrganizationRole Role { get; set; } = OrganizationRole.Owner;
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(requirements.OfType<OrganizationPermissionRequirement>()
                .All(requirement => OrganizationPermissions.IsAllowed(Role, requirement.Permission))
                    ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class MutableOrganizationScope : IOrganizationScope
    {
        public Guid? OrganizationId { get; set; }
    }

    private sealed class StubMoenvFactorSource(MoenvFactorDownload download) : IMoenvFactorSource
    {
        public Task<MoenvFactorDownload> DownloadAsync(CancellationToken cancellationToken)
            => Task.FromResult(download);
    }

    private static EmissionFactorVersionRecord CreateFactorVersion(
        Guid organizationId,
        Guid factorId,
        string name,
        decimal value,
        FactorPublicationStatus publicationStatus,
        FactorReviewStatus reviewStatus,
        string sourceReference,
        string originalDocumentName,
        string originalDocumentSha256) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FactorId = factorId,
            VersionNumber = 1,
            Name = name,
            Value = value,
            NumeratorUnitCode = "kgCO2e",
            DenominatorUnitCode = "kg",
            Geography = "TW",
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = null,
            PublicationStatus = publicationStatus.ToString(),
            SourceDatasetVersion = "CFP_P_02-2026",
            LicenseCode = "政府資料開放授權條款第1版",
            SourceType = "government-database",
            SourceName = "環境部",
            SourceReference = sourceReference,
            DatasetName = "環境部碳足跡排放係數",
            OriginalDocumentName = originalDocumentName,
            OriginalDocumentSha256 = originalDocumentSha256,
            Applicability = "測試適用性",
            ReviewStatus = reviewStatus.ToString()
        };

    private static PcrVersionRecord CreatePcrVersion(
        Guid organizationId,
        PcrPublicationStatus publicationStatus) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RuleSetId = Guid.NewGuid(),
            RegistrationNumber = $"PCR-{Guid.NewGuid():N}",
            VersionNumber = 1,
            Title = "PCR integration rule",
            ApprovalDate = new DateOnly(2026, 1, 1),
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2027, 12, 31),
            PublicationStatus = publicationStatus.ToString(),
            SourceReference = "https://example.test/pcr",
            StandardCode = "ISO 14067",
            CccClassification = "TEST",
            Applicability = "Integration test",
            RuleRequirements = "Test requirements",
            OriginalDocumentName = "pcr.pdf",
            OriginalDocumentObjectKey = $"test/{Guid.NewGuid():N}",
            OriginalDocumentContentType = "application/pdf",
            OriginalDocumentSizeBytes = 100,
            OriginalDocumentSha256 = new string('a', 64),
            OriginalDocumentScanStatus = "Clean",
            ProductCategoryPatterns = "*",
            FunctionalUnitPattern = "*",
            DeclaredUnitCode = "*",
            SystemBoundaryCode = "*",
            FormulaRuleSetVersion = "test-v1",
            ReportingRequirements = "Test reporting",
            ReviewStatus = PcrReviewStatus.Approved.ToString(),
            CustomApprovalStatus = PcrCustomApprovalStatus.NotRequired.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            PublishedAt = publicationStatus == PcrPublicationStatus.Published
                ? DateTimeOffset.UtcNow
                : null
        };
}
