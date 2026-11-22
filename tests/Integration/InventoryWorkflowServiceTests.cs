using System.Security.Claims;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Integration.Tests;

/// <summary>Inventory submit/review run without any Razor PageModel; readiness is stubbed so only the workflow rules are under test.</summary>
public sealed class InventoryWorkflowServiceTests
{
    [Fact]
    public async Task Submit_DeniedCallerChangesNothing()
    {
        var (organizationId, projectId, _) = await SeedAsync();
        await using var db = Db(organizationId);

        var result = await Service(db, organizationId, allowed: false, new StubReadiness())
            .SubmitAsync(Context(Guid.NewGuid()), projectId, Version(organizationId, projectId), [], default);

        Assert.Equal(OperationStatus.Forbidden, result.Status);
        await using var verify = Db(organizationId);
        Assert.Equal("Draft", (await verify.InventoryProjectVersions.AsNoTracking().SingleAsync()).WorkflowStatus);
        Assert.False(await verify.AuditEvents.AnyAsync());
    }

    [Fact]
    public async Task Submit_BlockingFindingsAreReturnedAsDetailsAndNothingChanges()
    {
        var (organizationId, projectId, _) = await SeedAsync();
        await using var db = Db(organizationId);
        var blocking = new ReadinessFinding("RDY-TEST", ReadinessSeverity.BlockingError, "InventoryProjectVersion", projectId.ToString(),
            null, string.Empty, "blocked", "fix it");

        var result = await Service(db, organizationId, allowed: true, new StubReadiness(new ReadinessReport([blocking], [], DateOnly.FromDateTime(DateTime.UtcNow))))
            .SubmitAsync(Context(Guid.NewGuid()), projectId, Version(organizationId, projectId), [], default);

        Assert.Equal(OperationStatus.Invalid, result.Status);
        Assert.Contains("RDY-TEST：blocked", result.Details!);
        await using var verify = Db(organizationId);
        Assert.Equal("Draft", (await verify.InventoryProjectVersions.AsNoTracking().SingleAsync()).WorkflowStatus);
    }

    [Fact]
    public async Task Submit_ThenApprove_RecordsReviewerAndAuditsBothTransitions()
    {
        var (organizationId, projectId, reviewer) = await SeedAsync();
        await using (var db = Db(organizationId))
        {
            var submitted = await Service(db, organizationId, allowed: true, new StubReadiness())
                .SubmitAsync(Context(Guid.NewGuid()), projectId, Version(organizationId, projectId), [], default);
            Assert.True(submitted.Succeeded);
        }
        await using (var db = Db(organizationId))
        {
            var approved = await Service(db, organizationId, allowed: true, new StubReadiness())
                .ReviewAsync(Context(reviewer), projectId, InventoryWorkflowStatus.Approved, " ok ", Version(organizationId, projectId), default);
            Assert.True(approved.Succeeded);
        }

        await using var verify = Db(organizationId);
        var stored = await verify.InventoryProjectVersions.AsNoTracking().SingleAsync();
        Assert.Equal("Approved", stored.WorkflowStatus);
        Assert.Equal(reviewer, stored.ReviewedBy);
        Assert.Equal("ok", stored.ReviewComment);
        Assert.Equal(1, await verify.AuditEvents.CountAsync(item => item.Action == "inventory.submitted"));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(item => item.Action == "inventory.approved"));
    }

    [Fact]
    public async Task Review_ApprovalIsRefusedWhenCalculationIsNotCurrent_AndChangesRequestedNeedsComment()
    {
        var (organizationId, projectId, reviewer) = await SeedAsync(status: "Submitted");
        await using var db = Db(organizationId);
        var service = Service(db, organizationId, allowed: true, new StubReadiness(runError: "stale run"));

        var approve = await service.ReviewAsync(Context(reviewer), projectId, InventoryWorkflowStatus.Approved, null, Version(organizationId, projectId), default);
        var noComment = await service.ReviewAsync(Context(reviewer), projectId, InventoryWorkflowStatus.ChangesRequested, " ", Version(organizationId, projectId), default);
        var wrongDecision = await service.ReviewAsync(Context(reviewer), projectId, InventoryWorkflowStatus.Submitted, null, Version(organizationId, projectId), default);

        Assert.Equal(OperationStatus.Invalid, approve.Status);
        Assert.Equal("stale run", approve.Message);
        Assert.Equal(OperationStatus.Invalid, noComment.Status);
        Assert.Equal(OperationStatus.BadRequest, wrongDecision.Status);
        await using var verify = Db(organizationId);
        Assert.Equal("Submitted", (await verify.InventoryProjectVersions.AsNoTracking().SingleAsync()).WorkflowStatus);
    }

    [Fact]
    public async Task Review_StaleVersionIsRejectedAsConflictAndAudited()
    {
        var (organizationId, projectId, reviewer) = await SeedAsync(status: "Submitted");
        var stale = Version(organizationId, projectId);
        await using (var editor = Db(organizationId))
        {
            (await editor.InventoryProjectVersions.SingleAsync()).FunctionalUnit = "edited elsewhere";
            await editor.SaveChangesAsync();
        }

        await using var db = Db(organizationId);
        var result = await Service(db, organizationId, allowed: true, new StubReadiness())
            .ReviewAsync(Context(reviewer), projectId, InventoryWorkflowStatus.ChangesRequested, "fix", stale, default);

        Assert.Equal(OperationStatus.Conflict, result.Status);
        await using var verify = Db(organizationId);
        Assert.Equal("Submitted", (await verify.InventoryProjectVersions.AsNoTracking().SingleAsync()).WorkflowStatus);
        Assert.Equal("concurrency.rejected", (await verify.AuditEvents.AsNoTracking().SingleAsync()).Action);
    }

    private static InventoryWorkflowService Service(CarbonFootprintDbContext db, Guid organizationId, bool allowed, IInventoryReadiness readiness) =>
        new(db, new Authorizer(allowed), new Scope(organizationId), readiness);

    private static uint Version(Guid organizationId, Guid projectId)
    {
        using var db = Db(organizationId);
        return db.InventoryProjectVersions.AsNoTracking().Single(item => item.Id == projectId).RowVersion;
    }

    private static async Task<(Guid OrganizationId, Guid ProjectId, Guid Reviewer)> SeedAsync(string status = "Draft")
    {
        var organizationId = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productVersionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        await using var seed = Db(organizationId);
        seed.Users.Add(new CarbonFootprint.Infrastructure.Identity.ApplicationUser { Id = reviewer, UserName = $"wf-{reviewer:N}@example.test", Email = $"wf-{reviewer:N}@example.test" });
        seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Workflow fixture" });
        seed.Products.Add(new ProductRecord { Id = productId, OrganizationId = organizationId, Name = "Workflow" });
        seed.ProductVersions.Add(new ProductVersionRecord
        {
            Id = productVersionId,
            OrganizationId = organizationId,
            ProductId = productId,
            VersionNumber = 1,
            NameZhTw = "Workflow"
        });
        seed.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
        {
            Id = projectId,
            OrganizationId = organizationId,
            ProductVersionId = productVersionId,
            VersionNumber = 1,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            FunctionalUnit = "1 kg product",
            PcrVersion = "none",
            WorkflowStatus = status
        });
        await seed.SaveChangesAsync();
        return (organizationId, projectId, reviewer);
    }

    private static OperationContext Context(Guid actor) =>
        new(new ClaimsPrincipal(new ClaimsIdentity()), actor, "wf-" + Guid.NewGuid().ToString("N"), null);

    private static DbContextOptions<CarbonFootprintDbContext> Options() => new DbContextOptionsBuilder<CarbonFootprintDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION") ?? throw new InvalidOperationException("Isolated database required"))
        .UseSnakeCaseNamingConvention().Options;
    private static CarbonFootprintDbContext Db(Guid organizationId) => new(Options(), new Scope(organizationId));
    private sealed record Scope(Guid Id) : IOrganizationScope { public Guid? OrganizationId => Id; }
    private sealed class Authorizer(bool allowed) : IOperationAuthorizer
    {
        public Task<bool> IsAllowedAsync(OperationContext context, OrganizationPermission permission, bool requireMfa) => Task.FromResult(allowed);
    }
    private sealed class StubReadiness(ReadinessReport? report = null, string? runError = null) : IInventoryReadiness
    {
        public Task<ReadinessReport> BuildReportAsync(Guid projectVersionId, IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken) =>
            Task.FromResult(report ?? new ReadinessReport([], [], DateOnly.FromDateTime(DateTime.UtcNow)));
        public Task<ReadinessAssessment> AssessAsync(Guid projectVersionId, IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken) =>
            Task.FromResult(new ReadinessAssessment(report ?? new ReadinessReport([], [], DateOnly.FromDateTime(DateTime.UtcNow)), runError));
        public Task<string?> GetCurrentRunErrorAsync(Guid projectVersionId, CancellationToken cancellationToken) => Task.FromResult(runError);
        public Task<IReadOnlyList<PcrRuleViolation>> ValidatePcrAsync(Guid projectVersionId, bool requireCompleteInventory, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PcrRuleViolation>>([]);
    }
}
