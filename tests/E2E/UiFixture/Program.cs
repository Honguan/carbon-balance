using System.Security.Cryptography;
using System.Text.Json;
using CarbonFootprint.Infrastructure;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set CARBON_TEST_DB_CONNECTION to an isolated, migrated test database.");
if (args.Length > 1)
    throw new ArgumentException("Usage: UiFixture [output-json-path]");
var output = Path.GetFullPath(args.FirstOrDefault() ?? ".analysis/ui-workflow.local.json");
if (File.Exists(output))
    throw new IOException($"Fixture output already exists; choose a new path: {output}");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var builder = WebApplication.CreateBuilder();
builder.Configuration["ConnectionStrings:Database"] = connectionString;
builder.Logging.ClearProviders();
builder.Services.AddCarbonFootprintInfrastructure(builder.Configuration, true);
await using var app = builder.Build();
await using var scope = app.Services.CreateAsyncScope();
var services = scope.ServiceProvider;
var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
var stamp = Guid.NewGuid().ToString("N")[..12];
var email = $"ui-workflow-{stamp}@example.test";
var password = $"Ui-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}!";
var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = "UI 回歸測試使用者" };
var created = await manager.CreateAsync(user, password);
if (!created.Succeeded)
    throw new InvalidOperationException(string.Join(";", created.Errors.Select(x => x.Description)));
var orgId = await services.GetRequiredService<OrganizationOnboardingService>().CreateAsync(user, $"UI 回歸測試 {stamp}", default);
var options = services.GetRequiredService<DbContextOptions<CarbonFootprintDbContext>>();
await using var db = new CarbonFootprintDbContext(options, new FixtureScope(orgId));
var now = DateTimeOffset.UtcNow;
var facility = new FacilityRecord { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "UI-FACTORY", Name = "測試廠場", CreatedAt = now };
var product = new ProductRecord { Id = Guid.NewGuid(), OrganizationId = orgId, FacilityId = facility.Id, Name = "UI 測試產品", CategoryCode = "TEST", CreatedAt = now };
var version = new ProductVersionRecord { Id = Guid.NewGuid(), OrganizationId = orgId, ProductId = product.Id, NameZhTw = product.Name, VersionNumber = 1, CreatedAt = now };
var pcr = new PcrVersionRecord
{
    Id = Guid.NewGuid(), OrganizationId = orgId, RuleSetId = Guid.NewGuid(), RegistrationNumber = "UI-TEST", VersionNumber = 1,
    Title = "UI 回歸規則（非正式 PCR）", ApprovalDate = new(2026, 1, 1), ValidFrom = new(2026, 1, 1), ValidTo = new(2027, 12, 31),
    PublicationStatus = "Published", ReviewStatus = "Approved", SourceReference = "https://example.test/pcr", StandardCode = "test-only",
    Applicability = "隔離 UI 測試", OriginalDocumentName = "test-only.txt", OriginalDocumentObjectKey = "test-only", OriginalDocumentContentType = "text/plain",
    OriginalDocumentSizeBytes = 1, OriginalDocumentSha256 = new string('a',64), OriginalDocumentScanStatus = "Clean",
    DeclaredUnitCode = "kg", SystemBoundaryCode = "cradle-to-grave", PermittedAllocationMethodsCsv = "mass", FormulaRuleSetVersion = ActivityEmissionFormula.PcrFormulaRuleSetV1,
    ReportingRequirements = "UI 測試", CreatedAt = now, PublishedAt = now
};
db.AddRange(facility, product, version, pcr);
foreach (var stage in Enum.GetValues<LifecycleStage>()) db.PcrStageRules.Add(new PcrStageRuleRecord { Id = Guid.NewGuid(), OrganizationId = orgId, PcrVersionId = pcr.Id, LifecycleStage = (int)stage, Requirement = "Optional" });
var factors = new[] { "kg", "kWh", "tonne-km" }.Select(unit => new EmissionFactorVersionRecord
{
    Id = Guid.NewGuid(), OrganizationId = orgId, FactorId = Guid.NewGuid(), VersionNumber = 1, Name = $"測試係數 {unit}", Value = 2m,
    NumeratorUnitCode = "kgCO2e", DenominatorUnitCode = unit, Geography = "TW", ValidFrom = new(2026,1,1), ValidTo = new(2027,12,31),
    PublicationStatus = "Published", ReviewStatus = "Approved", SourceDatasetVersion = "ui-test-v1", LicenseCode = "test-only",
    SourceType = "test", SourceName = "UI 回歸測試", SourceReference = "https://example.test/factor", DatasetName = "UI fixture", OriginalDocumentName = "fixture.csv", OriginalDocumentSha256 = new string('b',64), Applicability = "隔離測試", PublishedAt = now
}).ToArray();
db.EmissionFactorVersions.AddRange(factors);
var projects = new[] { "A", "B" }.Select((label, index) => new InventoryProjectVersionRecord
{
    Id = Guid.NewGuid(), OrganizationId = orgId, ProductVersionId = version.Id, VersionNumber = index+1,
    PeriodStart = new(2026,1,1), PeriodEnd = new(2026,12,31), FunctionalUnit = $"UI 盤查 {label} 1 kg", DeclaredUnit = "kg", SystemBoundary = "cradle-to-grave",
    AllocationMethod = "mass", AllocationReason = "測試質量分配", PcrVersionId = pcr.Id, PcrVersion = "UI-TEST-v1", WorkflowStatus = "Draft", CreatedAt = now.AddSeconds(index)
}).ToArray();
db.InventoryProjectVersions.AddRange(projects);
foreach (var project in projects)
{
    foreach(var stage in Enum.GetValues<LifecycleStage>()) db.LifecycleStageDeclarations.Add(new LifecycleStageDeclarationRecord { Id = Guid.NewGuid(), OrganizationId = orgId, InventoryProjectVersionId = project.Id, LifecycleStage = (int)stage, IsApplicable = true });
    db.ActivityData.Add(new ActivityDataRecord
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, InventoryProjectVersionId = project.Id, LifecycleStage = (int)LifecycleStage.RawMaterial,
        Name = $"可更正原料 {project.VersionNumber}", ActivityKind = "Material", RawValue = 10m, RawUnitCode = "kg", CanonicalValue = 10m, CanonicalUnitCode = "kg",
        ConversionRuleVersion = "units-p0-v1", AmountFormulaId = ActivityAmountFormula.DirectFormulaId, FormulaInputsJson = "{\"value\":10}",
        PeriodStart = project.PeriodStart, PeriodEnd = project.PeriodEnd, FactorVersionId = factors[0].Id, AllocationFactor = 1m,
        DataQuality = "primary", DataSourceType = "一級數據－直接量測", DataProvider = "本組織／廠場", CollectionMethod = "直接量測", SourceReference = "ui-fixture"
    });
}
await db.SaveChangesAsync();
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { email, password, organizationId = orgId, projectA = projects[0].Id, projectB = projects[1].Id, factorId = factors[0].Id }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Fixture ready: {output}");
sealed record FixtureScope(Guid Value) : IOrganizationScope { public Guid? OrganizationId => Value; }
