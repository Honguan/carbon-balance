using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Verification;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web;

public static class GovernanceApi
{
    public static IEndpointRouteBuilder MapGovernanceApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/governance")
            .RequireAuthorization();

        group.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = tokens.RequestToken, headerName = tokens.HeaderName });
        });

        group.MapGet("/projects/{projectVersionId:guid}/overview", GetOverviewAsync);
        group.MapGet("/projects/{projectVersionId:guid}/readiness", GetReadinessAsync);
        group.MapPost("/projects/{projectVersionId:guid}/acknowledgements", AcknowledgeAsync);
        group.MapPost("/projects/{projectVersionId:guid}/transitions", TransitionAsync);
        group.MapPost("/projects/{projectVersionId:guid}/archives/{calculationRunId:guid}", GenerateArchiveAsync);
        group.MapGet("/archives/{archiveId:guid}", DownloadArchiveAsync);
        group.MapGet("/evidence/{documentVersionId:guid}", DownloadEvidenceAsync);
        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        Guid projectVersionId,
        GovernanceWorkspaceService governance,
        IAuthorizationService authorization,
        HttpContext context)
    {
        if (!await IsAllowedAsync(authorization, context, OrganizationPermission.ViewInventory))
        {
            return Results.Forbid();
        }

        return Results.Ok(await governance.GetOverviewAsync(projectVersionId, context.RequestAborted));
    }

    private static async Task<IResult> GetReadinessAsync(
        Guid projectVersionId,
        GovernanceWorkspaceService governance,
        IAuthorizationService authorization,
        GovernanceAccessService access,
        HttpContext context)
    {
        if (!await IsAllowedAsync(authorization, context, OrganizationPermission.ViewInventory))
        {
            return Results.Forbid();
        }

        var actor = await access.ResolveAsync(context.User, context.RequestAborted);
        if (actor is null)
        {
            return Results.Forbid();
        }

        var acknowledgements = await governance.GetAcknowledgedRuleCodesAsync(projectVersionId, context.RequestAborted);
        var report = await governance.BuildReadinessReportAsync(
            projectVersionId,
            acknowledgements,
            actor.UserId,
            persist: false,
            context.RequestAborted);
        return Results.Ok(new
        {
            report.ProjectVersionId,
            report.IsReady,
            canSubmit = report.CanSubmit(acknowledgements),
            acknowledgements,
            report.Results
        });
    }

    private static async Task<IResult> AcknowledgeAsync(
        Guid projectVersionId,
        ReadinessAcknowledgementRequest request,
        GovernanceWorkspaceService governance,
        IAuthorizationService authorization,
        GovernanceAccessService access,
        IAntiforgery antiforgery,
        HttpContext context)
    {
        await antiforgery.ValidateRequestAsync(context);
        if (!await IsAllowedAsync(authorization, context, OrganizationPermission.EditInventory))
        {
            return Results.Forbid();
        }

        var actor = await access.ResolveAsync(context.User, context.RequestAborted);
        if (actor is null)
        {
            return Results.Forbid();
        }

        await governance.AcknowledgeReadinessRuleAsync(
            projectVersionId,
            request.RuleCode,
            request.Explanation,
            actor.UserId,
            context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> TransitionAsync(
        Guid projectVersionId,
        WorkflowTransitionApiRequest request,
        GovernanceWorkspaceService governance,
        IAuthorizationService authorization,
        GovernanceAccessService access,
        IAntiforgery antiforgery,
        HttpContext context)
    {
        await antiforgery.ValidateRequestAsync(context);
        if (!Enum.TryParse<VerificationWorkflowState>(request.TargetState, ignoreCase: true, out var targetState))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.TargetState)] = ["Unknown verification workflow state."]
            });
        }

        var permission = targetState is VerificationWorkflowState.VerificationRequested
            or VerificationWorkflowState.UnderVerification
            or VerificationWorkflowState.Verified
            ? OrganizationPermission.VerifyInventory
            : targetState is VerificationWorkflowState.InReview
                or VerificationWorkflowState.ChangesRequested
                or VerificationWorkflowState.InternallyApproved
                or VerificationWorkflowState.Rejected
                ? OrganizationPermission.ReviewInventory
                : OrganizationPermission.EditInventory;
        if (!await IsAllowedAsync(authorization, context, permission))
        {
            return Results.Forbid();
        }

        var actor = await access.ResolveAsync(context.User, context.RequestAborted);
        if (actor is null)
        {
            return Results.Forbid();
        }

        var result = await governance.TransitionAsync(
            projectVersionId,
            targetState,
            actor.UserId,
            actor.WorkflowRoles,
            actor.HasVerifiedMfa,
            request.Reason,
            context.RequestAborted);
        return Results.Ok(result);
    }

    private static async Task<IResult> GenerateArchiveAsync(
        Guid projectVersionId,
        Guid calculationRunId,
        GovernanceWorkspaceService governance,
        IAuthorizationService authorization,
        GovernanceAccessService access,
        IAntiforgery antiforgery,
        HttpContext context)
    {
        await antiforgery.ValidateRequestAsync(context);
        if (!await IsAllowedAsync(authorization, context, OrganizationPermission.ReviewInventory))
        {
            return Results.Forbid();
        }

        var actor = await access.ResolveAsync(context.User, context.RequestAborted);
        if (actor is null || !actor.HasVerifiedMfa)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Verified MFA required",
                detail: "Verification archives require a two-factor authenticated session.");
        }

        var archive = await governance.GenerateVerificationArchiveAsync(
            projectVersionId,
            calculationRunId,
            actor.UserId,
            context.RequestAborted);
        return Results.Created($"/api/governance/archives/{archive.Id:D}", new
        {
            archive.Id,
            archive.ProjectVersionId,
            archive.CalculationRunId,
            archive.ExportSchemaVersion,
            archive.ArchiveSha256,
            archive.GeneratedAt
        });
    }

    private static async Task<IResult> DownloadArchiveAsync(
        Guid archiveId,
        CarbonFootprintDbContext dbContext,
        IAuthorizationService authorization,
        HttpContext context)
    {
        if (!await IsAllowedAsync(authorization, context, OrganizationPermission.ViewInventory))
        {
            return Results.Forbid();
        }

        var archive = await dbContext.VerificationArchives.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == archiveId, context.RequestAborted);
        return archive is null
            ? Results.NotFound()
            : Results.File(
                archive.ArchiveBytes,
                "application/zip",
                $"verification-archive-{archive.ProjectVersionId:N}-{archive.CalculationRunId:N}.zip");
    }

    private static async Task<IResult> DownloadEvidenceAsync(
        Guid documentVersionId,
        GovernanceWorkspaceService governance,
        CarbonFootprintDbContext dbContext,
        IAuthorizationService authorization,
        GovernanceAccessService access,
        HttpContext context)
    {
        if (!await IsAllowedAsync(authorization, context, OrganizationPermission.ViewInventory))
        {
            return Results.Forbid();
        }

        var actor = await access.ResolveAsync(context.User, context.RequestAborted);
        var version = await dbContext.EvidenceDocumentVersions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == documentVersionId, context.RequestAborted);
        if (actor is null || version is null)
        {
            return Results.NotFound();
        }

        var ipAddressHash = EvidenceRequestHash.Create(context.Connection.RemoteIpAddress?.ToString());
        var bytes = await governance.DownloadEvidenceAsync(
            documentVersionId,
            actor.UserId,
            ipAddressHash,
            context.RequestAborted);
        return Results.File(bytes, version.ContentType, version.OriginalFileName);
    }

    private static async Task<bool> IsAllowedAsync(
        IAuthorizationService authorization,
        HttpContext context,
        OrganizationPermission permission) =>
        (await authorization.AuthorizeAsync(
            context.User,
            resource: null,
            new OrganizationPermissionRequirement(permission))).Succeeded;
}

public sealed record ReadinessAcknowledgementRequest(string RuleCode, string Explanation);

public sealed record WorkflowTransitionApiRequest(string TargetState, string Reason);
