using System.Data.Common;
using System.Diagnostics;
using System.Security.Claims;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Pages;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace CarbonFootprint.Integration.Tests;

/// <summary>#49: with 120 projects, 10,000 activities and 10,000 factors the workspace reads only what the section renders.</summary>
public sealed class WorkspaceQueryScopeTests(ITestOutputHelper output)
{
    private const int ProjectCount = 120;
    private const int FactorCount = 10_000;
    private const int ActivitiesPerProject = 84; // 120 × 84 = 10,080 activities
    private const double LatencyBudgetMilliseconds = 3000;

    [Fact]
    public async Task EachSection_ReadsBoundedProjectScopedDataWithinQueryAndLatencyBudgets()
    {
        var organizationId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var projectIds = await SeedAsync(organizationId, reviewerId);
        var selected = projectIds[^1]; // newest project; the picker lists the newest 100 first

        var governance = await LoadAsync(organizationId, reviewerId, "governance", selected);
        Assert.InRange(governance.Queries, 1, 14);
        Assert.Empty(governance.Page.Activities);
        Assert.Empty(governance.Page.Factors);
        Assert.Empty(governance.Page.EvidenceFiles);
        Assert.Equal(ProjectCount, governance.Page.InventoryProjectTotal);
        Assert.Equal(CarbonFootprint.Web.Services.WorkspaceQueries.MaxListedProjects, governance.Page.InventoryProjects.Count);
        Assert.Contains(selected, governance.Page.InventoryProjects.Select(item => item.Id));

        // A project outside the newest 100 is still reachable when requested explicitly.
        var oldest = await LoadAsync(organizationId, reviewerId, "governance", projectIds[0]);
        Assert.Equal(projectIds[0], oldest.Page.ProjectVersionId);
        Assert.Contains(projectIds[0], oldest.Page.InventoryProjects.Select(item => item.Id));

        var factors = await LoadAsync(organizationId, reviewerId, "factors", selected);
        Assert.InRange(factors.Queries, 1, 20);
        Assert.Equal(CarbonFootprint.Web.Services.WorkspaceQueries.FactorPageSize, factors.Page.Factors.Count);
        Assert.Equal(FactorCount, factors.Page.FactorTotal);
        Assert.Equal(FactorCount / CarbonFootprint.Web.Services.WorkspaceQueries.FactorPageSize, factors.Page.FactorPageCount);
        Assert.Empty(factors.Page.Activities);
        var second = await LoadAsync(organizationId, reviewerId, "factors", selected, factorPage: 2);
        Assert.Empty(factors.Page.Factors.Select(item => item.Id).Intersect(second.Page.Factors.Select(item => item.Id)));
        var searched = await LoadAsync(organizationId, reviewerId, "factors", selected, factorQuery: "Needle_%");
        Assert.Equal(1, searched.Page.FactorTotal);

        var lifecycle = await LoadAsync(organizationId, reviewerId, "lifecycle", selected, stage: "raw-material");
        Assert.InRange(lifecycle.Queries, 1, 25);
        Assert.Equal(ActivitiesPerProject, lifecycle.Page.Activities.Count);
        Assert.All(lifecycle.Page.Activities, item => Assert.Equal(selected, item.InventoryProjectVersionId));
        Assert.Equal(CarbonFootprint.Web.Services.WorkspaceQueries.MaxSelectableFactors, lifecycle.Page.SelectableFactors.Count);
        Assert.True(lifecycle.Page.SelectableFactorTotal > lifecycle.Page.SelectableFactors.Count);
        Assert.True(lifecycle.Page.ReferencedFactors.Count <= ActivitiesPerProject);
        Assert.All(lifecycle.Page.EvidenceFiles, item => Assert.Contains(item.ActivityDataId, lifecycle.Page.Activities.Select(a => a.Id)));
        Assert.Empty(lifecycle.Page.Factors);

        var calculation = await LoadAsync(organizationId, reviewerId, "calculation", selected);
        Assert.InRange(calculation.Queries, 1, 40);
        Assert.Empty(calculation.Page.Activities);
        Assert.Equal(1, calculation.Page.SavedStageCount);
        Assert.NotNull(calculation.Page.Readiness);

        foreach (var (name, result) in new[] { ("governance", governance), ("factors", factors), ("lifecycle", lifecycle), ("calculation", calculation) })
        {
            output.WriteLine($"{name}: {result.Queries} queries, {result.Milliseconds:F0} ms");
            Assert.True(result.Milliseconds < LatencyBudgetMilliseconds, $"{name} took {result.Milliseconds:F0} ms (budget {LatencyBudgetMilliseconds:F0} ms).");
        }
    }

    private static async Task<List<Guid>> SeedAsync(Guid organizationId, Guid reviewerId)
    {
        var productId = Guid.NewGuid();
        var productVersionId = Guid.NewGuid();
        await using (var db = Db(organizationId))
        {
            db.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Scope fixture" });
            db.Users.Add(new ApplicationUser { Id = reviewerId, UserName = $"scope-{reviewerId:N}" });
            db.Products.Add(new ProductRecord { Id = productId, OrganizationId = organizationId, Name = "Scope" });
            db.ProductVersions.Add(new ProductVersionRecord
            {
                Id = productVersionId, OrganizationId = organizationId, ProductId = productId, VersionNumber = 1, NameZhTw = "Scope"
            });
            await db.SaveChangesAsync();
        }

        var factorIds = new List<Guid>();
        for (var batch = 0; batch < FactorCount; batch += 1000)
        {
            await using var db = Db(organizationId);
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            for (var index = batch; index < batch + 1000; index++)
            {
                var id = Guid.NewGuid();
                factorIds.Add(id);
                db.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
                {
                    Id = id, OrganizationId = organizationId, FactorId = Guid.NewGuid(), VersionNumber = 1,
                    Name = index == 4321 ? "Needle_% factor" : $"Factor {index:D5}", Value = 1m,
                    NumeratorUnitCode = "kgCO2e", DenominatorUnitCode = "kg", Geography = "TW", ValidFrom = new DateOnly(2026, 1, 1),
                    PublicationStatus = "Published", SourceDatasetVersion = "v1", LicenseCode = "CC0", SourceType = "government-database",
                    SourceName = "MOENV", SourceReference = "https://example.test/factor", DatasetName = "dataset",
                    OriginalDocumentName = "factor.csv", OriginalDocumentSha256 = new string('a', 64), Applicability = "global",
                    ReviewStatus = "Approved"
                });
            }
            await db.SaveChangesAsync();
        }

        var projectIds = new List<Guid>();
        for (var projectIndex = 0; projectIndex < ProjectCount; projectIndex++)
        {
            await using var db = Db(organizationId);
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var projectId = Guid.NewGuid();
            projectIds.Add(projectId);
            db.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
            {
                Id = projectId, OrganizationId = organizationId, ProductVersionId = productVersionId, VersionNumber = projectIndex + 1,
                PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 12, 31), FunctionalUnit = "1 kg product",
                PcrVersion = "none", WorkflowStatus = "Draft", CreatedAt = DateTimeOffset.UtcNow.AddSeconds(projectIndex)
            });
            for (var index = 0; index < ActivitiesPerProject; index++)
            {
                var activityId = Guid.NewGuid();
                db.ActivityData.Add(new ActivityDataRecord
                {
                    Id = activityId, OrganizationId = organizationId, InventoryProjectVersionId = projectId,
                    LifecycleStage = (int)LifecycleStage.RawMaterial, Name = $"Material {index:D3}", ActivityKind = "Material",
                    RawValue = 1m, RawUnitCode = "kg", CanonicalValue = 1m, CanonicalUnitCode = "kg",
                    AmountFormulaId = ActivityAmountFormula.DirectFormulaId, FormulaInputsJson = "{}", ConversionRuleVersion = "units-p0-v2",
                    PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 12, 31),
                    FactorVersionId = factorIds[(projectIndex * ActivitiesPerProject + index) % factorIds.Count],
                    AllocationFactor = 1m, EvidenceSha256 = new string('b', 64), DataQuality = "measured"
                });
                if (index % 4 == 0)
                {
                    db.EvidenceFiles.Add(new EvidenceFileRecord
                    {
                        Id = Guid.NewGuid(), OrganizationId = organizationId, ActivityDataId = activityId, ObjectKey = $"scope/{activityId:N}",
                        OriginalFileName = "evidence.pdf", ContentType = "application/pdf", SizeBytes = 1, Sha256 = new string('b', 64),
                        ScanStatus = "Clean", CreatedAt = DateTimeOffset.UtcNow
                    });
                }
            }
            await db.SaveChangesAsync();
        }
        return projectIds;
    }

    private static async Task<(WorkspaceModel Page, int Queries, double Milliseconds)> LoadAsync(
        Guid organizationId, Guid actorId, string section, Guid projectId, string? stage = null, int factorPage = 1, string? factorQuery = null)
    {
        var counter = new QueryCounter();
        await using var db = new CarbonFootprintDbContext(
            new DbContextOptionsBuilder<CarbonFootprintDbContext>(Options()).AddInterceptors(counter).Options, new Scope(organizationId));
        using var services = new ServiceCollection().AddSingleton(db).AddLogging()
            .AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<CarbonFootprintDbContext>().Services.BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId.ToString())], "test"))
        };
        var authorization = new AllowAll();
        var page = new WorkspaceModel(db, new Scope(organizationId), null!,
            services.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, new InventorySnapshotReader(db),
            authorization, null!, null!,
            CalculationBuildProvenance.Create("scope-test", new string('c', 40)), null!, null!,
            InventoryServices.Workflow(db, authorization, new Scope(organizationId)), InventoryServices.Readiness(db), InventoryServices.Activities(db, authorization, new Scope(organizationId)), InventoryServices.Organization(db, authorization, new Scope(organizationId), null!, services.GetRequiredService<UserManager<ApplicationUser>>()), InventoryServices.Products(db, authorization, new Scope(organizationId)))
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoTempData()),
            Section = section, Stage = stage, ProjectVersionId = projectId, FactorPage = factorPage, FactorQuery = factorQuery
        };
        await page.OnGetAsync(default); // warm-up compiles the queries so the measured run is steady-state
        counter.Reset();
        var watch = Stopwatch.StartNew();
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        watch.Stop();
        return (page, counter.Count, watch.Elapsed.TotalMilliseconds);
    }

    private static DbContextOptions<CarbonFootprintDbContext> Options() => new DbContextOptionsBuilder<CarbonFootprintDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION") ?? throw new InvalidOperationException("Isolated database required"))
        .UseSnakeCaseNamingConvention().Options;
    private static CarbonFootprintDbContext Db(Guid organizationId) => new(Options(), new Scope(organizationId));
    private sealed record Scope(Guid Id) : IOrganizationScope { public Guid? OrganizationId => Id; }
    private sealed class AllowAll : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(AuthorizationResult.Success());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Task.FromResult(AuthorizationResult.Success());
    }
    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public void Reset() => Count = 0;
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Count++;
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
