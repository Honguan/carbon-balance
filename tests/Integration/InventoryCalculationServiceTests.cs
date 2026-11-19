using System.Security.Claims;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Integration.Tests;

/// <summary>Calculation guards run without a Razor PageModel; every rejection must leave no run and no audit behind.</summary>
public sealed class InventoryCalculationServiceTests
{
    [Fact]
    public async Task DeniedCaller_IsForbidden()
    {
        var (organizationId, projectId) = await SeedAsync("Draft", withPcr: true);
        var result = await CalculateAsync(organizationId, projectId, allowed: false, []);

        Assert.Equal(OperationStatus.Forbidden, result.Status);
        await AssertNothingWrittenAsync(organizationId);
    }

    [Fact]
    public async Task UnknownProject_IsNotFound()
    {
        var (organizationId, _) = await SeedAsync("Draft", withPcr: true);
        Assert.Equal(OperationStatus.NotFound, (await CalculateAsync(organizationId, Guid.NewGuid(), allowed: true, [])).Status);
    }

    [Theory]
    [InlineData("Submitted", true, "盤查已送審或核准，不可建立新計算版本。")]
    [InlineData("Approved", true, "盤查已送審或核准，不可建立新計算版本。")]
    [InlineData("Draft", false, "盤查版本未綁定受治理的 PCR 版本。")]
    public async Task LockedOrUnboundInventory_IsRejected(string status, bool withPcr, string message)
    {
        var (organizationId, projectId) = await SeedAsync(status, withPcr);
        var result = await CalculateAsync(organizationId, projectId, allowed: true, []);

        Assert.Equal(OperationStatus.Invalid, result.Status);
        Assert.Equal("calculation", result.Key);
        Assert.Equal(message, result.Message);
        await AssertNothingWrittenAsync(organizationId);
    }

    [Fact]
    public async Task PcrViolations_AreReturnedAsDetails()
    {
        var (organizationId, projectId) = await SeedAsync("Draft", withPcr: true);
        var result = await CalculateAsync(organizationId, projectId, allowed: true, [new PcrRuleViolation("PCR-TEST", "InventoryProjectVersion", projectId.ToString(), "missing stage")]);

        Assert.Equal(OperationStatus.Invalid, result.Status);
        Assert.Equal(["PCR-TEST：missing stage"], result.Details!);
        await AssertNothingWrittenAsync(organizationId);
    }

    // Guards return before the snapshot reader and handler are touched, so they are not needed here.
    private static async Task<OperationResult> CalculateAsync(
        Guid organizationId, Guid projectId, bool allowed, IReadOnlyList<PcrRuleViolation> violations)
    {
        await using var db = Db(organizationId);
        return await new InventoryCalculationService(db, new Authorizer(allowed), new StubReadiness(violations), null!, null!, null!)
            .CalculateAsync(new OperationContext(new ClaimsPrincipal(new ClaimsIdentity()), Guid.NewGuid(), "calc-" + Guid.NewGuid().ToString("N"), null),
                projectId, default);
    }

    private static async Task AssertNothingWrittenAsync(Guid organizationId)
    {
        await using var verify = Db(organizationId);
        Assert.False(await verify.CalculationRuns.AnyAsync());
        Assert.False(await verify.AuditEvents.AnyAsync());
    }

    private static async Task<(Guid OrganizationId, Guid ProjectId)> SeedAsync(string status, bool withPcr)
    {
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productVersionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var pcrId = Guid.NewGuid();
        await using var seed = Db(organizationId);
        seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Calculation fixture" });
        seed.Products.Add(new ProductRecord { Id = productId, OrganizationId = organizationId, Name = "Calculation" });
        seed.ProductVersions.Add(new ProductVersionRecord
        {
            Id = productVersionId, OrganizationId = organizationId, ProductId = productId, VersionNumber = 1, NameZhTw = "Calculation"
        });
        if (withPcr)
        {
            seed.PcrVersions.Add(new PcrVersionRecord
            {
                Id = pcrId, OrganizationId = organizationId, RuleSetId = Guid.NewGuid(), RegistrationNumber = $"PCR-{pcrId:N}",
                VersionNumber = 1, Title = "Calculation PCR", PublicationStatus = "Draft", SourceReference = "https://example.test/pcr"
            });
        }
        seed.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
        {
            Id = projectId, OrganizationId = organizationId, ProductVersionId = productVersionId, VersionNumber = 1,
            PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 12, 31),
            FunctionalUnit = "1 kg product", PcrVersion = "none", WorkflowStatus = status, PcrVersionId = withPcr ? pcrId : null
        });
        await seed.SaveChangesAsync();
        return (organizationId, projectId);
    }

    private static DbContextOptions<CarbonFootprintDbContext> Options() => new DbContextOptionsBuilder<CarbonFootprintDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION") ?? throw new InvalidOperationException("Isolated database required"))
        .UseSnakeCaseNamingConvention().Options;
    private static CarbonFootprintDbContext Db(Guid organizationId) => new(Options(), new Scope(organizationId));
    private sealed record Scope(Guid Id) : IOrganizationScope { public Guid? OrganizationId => Id; }
    private sealed class Authorizer(bool allowed) : IOperationAuthorizer
    {
        public Task<bool> IsAllowedAsync(OperationContext context, OrganizationPermission permission, bool requireMfa) => Task.FromResult(allowed);
    }
    private sealed class StubReadiness(IReadOnlyList<PcrRuleViolation> violations) : IInventoryReadiness
    {
        public Task<ReadinessReport> BuildReportAsync(Guid projectVersionId, IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ReadinessAssessment> AssessAsync(Guid projectVersionId, IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<string?> GetCurrentRunErrorAsync(Guid projectVersionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PcrRuleViolation>> ValidatePcrAsync(Guid projectVersionId, bool requireCompleteInventory, CancellationToken cancellationToken) =>
            Task.FromResult(violations);
    }
}
