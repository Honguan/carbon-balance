using CarbonFootprint.Domain.Modules.Evidence;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Readiness;
using CarbonFootprint.Domain.Modules.Verification;
using CarbonFootprint.Infrastructure.Evidence;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Security;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Pages;

[Authorize]
public sealed class GovernanceModel : PageModel
{
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly GovernanceWorkspaceService _governance;
    private readonly GovernanceAccessService _access;
    private readonly EvidenceStorageService _evidenceStorage;
    private readonly IAuthorizationService _authorization;
    private readonly IOrganizationScope _organizationScope;

    public GovernanceModel(
        CarbonFootprintDbContext dbContext,
        GovernanceWorkspaceService governance,
        GovernanceAccessService access,
        EvidenceStorageService evidenceStorage,
        IAuthorizationService authorization,
        IOrganizationScope organizationScope)
    {
        _dbContext = dbContext;
        _governance = governance;
        _access = access;
        _evidenceStorage = evidenceStorage;
        _authorization = authorization;
        _organizationScope = organizationScope;
    }

    [BindProperty(SupportsGet = true)]
    public Guid? ProjectVersionId { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public IReadOnlyList<InventoryProjectVersionRecord> Projects { get; private set; } = [];
    public IReadOnlyList<ActivityDataRecord> Activities { get; private set; } = [];
    public IReadOnlyList<CalculationRunRecord> Runs { get; private set; } = [];
    public GovernanceOverview? Overview { get; private set; }
    public InventoryReadinessReport? Readiness { get; private set; }
    public IReadOnlySet<string> AcknowledgedRuleCodes { get; private set; } = new HashSet<string>();
    public bool CanEdit { get; private set; }
    public bool CanManage { get; private set; }
    public bool CanReview { get; private set; }
    public bool CanVerify { get; private set; }
    public bool HasVerifiedMfa { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_organizationScope.OrganizationId.HasValue)
        {
            return RedirectToPage("/Workspace", new { section = "governance" });
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveDefinitionAsync(
        Guid? projectVersionId,
        string definitionType,
        string stableKey,
        string name,
        string payloadJson,
        string? sourceStableId,
        string? sourceName,
        string? sourceUrl,
        string? sourceDatasetVersion,
        string? licenseCode,
        DateOnly? validFrom,
        DateOnly? validTo,
        Guid? sourceEvidenceDocumentVersionId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.ManageGovernance, async actor =>
        {
            await _governance.SaveDefinitionAsync(
                definitionType,
                stableKey,
                name,
                payloadJson,
                _organizationScope.OrganizationId,
                sourceStableId ?? string.Empty,
                sourceName ?? string.Empty,
                sourceUrl ?? string.Empty,
                sourceDatasetVersion ?? string.Empty,
                licenseCode ?? string.Empty,
                validFrom,
                validTo,
                sourceEvidenceDocumentVersionId,
                actor.UserId,
                cancellationToken);
            return "治理定義草稿已建立。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostPublishDefinitionAsync(
        Guid? projectVersionId,
        Guid definitionVersionId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.ManageGovernance, async actor =>
        {
            await _governance.PublishDefinitionAsync(definitionVersionId, actor.UserId, cancellationToken);
            return "治理定義已發布並保留版本鏈。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostWithdrawDefinitionAsync(
        Guid? projectVersionId,
        Guid definitionVersionId,
        string reason,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.ManageGovernance, async _ =>
        {
            await _governance.WithdrawDefinitionAsync(definitionVersionId, reason, cancellationToken);
            return "治理定義已撤回。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostActivateDefinitionAsync(
        Guid? projectVersionId,
        Guid definitionVersionId,
        bool enabled,
        bool prohibited,
        string? displayAlias,
        string? internalCategory,
        string? applicabilityNote,
        string? overridePayloadJson,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.ManageGovernance, async actor =>
        {
            await _governance.ActivateDefinitionAsync(
                definitionVersionId,
                enabled,
                prohibited,
                displayAlias ?? string.Empty,
                internalCategory ?? string.Empty,
                applicabilityNote ?? string.Empty,
                string.IsNullOrWhiteSpace(overridePayloadJson) ? "{}" : overridePayloadJson,
                actor.UserId,
                cancellationToken);
            return "組織治理定義啟用狀態已更新。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostSaveDataQualityAsync(
        Guid projectVersionId,
        Guid activityId,
        Guid ruleSetDefinitionVersionId,
        string assessmentJson,
        string? uncertaintyInputsJson,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.EditInventory, async actor =>
        {
            await _governance.SaveDataQualityAssessmentAsync(
                projectVersionId,
                activityId,
                ruleSetDefinitionVersionId,
                assessmentJson,
                string.IsNullOrWhiteSpace(uncertaintyInputsJson) ? "null" : uncertaintyInputsJson,
                actor.UserId,
                cancellationToken);
            return "資料品質與不確定性評估已保存並寫入不可變軌跡。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostSaveAllocationAsync(
        Guid projectVersionId,
        string poolJson,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.EditInventory, async actor =>
        {
            await _governance.SaveAllocationPoolAsync(projectVersionId, poolJson, actor.UserId, cancellationToken);
            return "分配池與計算結果已保存。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostSaveTransportAsync(
        Guid projectVersionId,
        string chainJson,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.EditInventory, async actor =>
        {
            await _governance.SaveTransportChainAsync(projectVersionId, chainJson, actor.UserId, cancellationToken);
            return "多段運輸鏈與 TTW/WTT/WTW 軌跡已保存。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostBindFormulaAsync(
        Guid projectVersionId,
        Guid activityId,
        Guid formulaDefinitionVersionId,
        string formulaValuesJson,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.EditInventory, async actor =>
        {
            await _governance.BindFormulaAsync(
                projectVersionId,
                activityId,
                formulaDefinitionVersionId,
                formulaValuesJson,
                actor.UserId,
                cancellationToken);
            return "活動公式版本與輸入值已綁定。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostBindGlobalFactorAsync(
        Guid projectVersionId,
        Guid activityId,
        Guid globalFactorDefinitionVersionId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.EditInventory, async actor =>
        {
            await _governance.BindGlobalFactorAsync(
                projectVersionId,
                activityId,
                globalFactorDefinitionVersionId,
                actor.UserId,
                cancellationToken);
            return "全域係數版本已綁定並保留歷史引用。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostUploadEvidenceAsync(
        Guid projectVersionId,
        IFormFile file,
        Guid? existingDocumentId,
        string title,
        EvidenceCategory category,
        DateOnly? coverageStart,
        DateOnly? coverageEnd,
        bool isSensitive,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.EditInventory, async actor =>
        {
            var organizationId = _organizationScope.OrganizationId
                ?? throw new InvalidOperationException("缺少組織範圍。");
            await using var stream = file.OpenReadStream();
            var stored = await _evidenceStorage.StoreAsync(
                organizationId,
                stream,
                file.FileName,
                file.ContentType,
                cancellationToken);
            var result = await _governance.RegisterEvidenceAsync(
                existingDocumentId,
                title,
                category,
                coverageStart,
                coverageEnd,
                isSensitive,
                stored,
                actor.UserId,
                cancellationToken);
            return $"佐證文件版本 {result.Version.VersionNumber} 已完成 SHA-256 與惡意程式掃描。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostLinkEvidenceAsync(
        Guid projectVersionId,
        Guid documentVersionId,
        EvidenceLinkTargetType targetType,
        Guid targetId,
        string? purpose,
        bool isRequired,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.EditInventory, async actor =>
        {
            await _governance.LinkEvidenceAsync(
                documentVersionId,
                targetType,
                targetId,
                purpose ?? string.Empty,
                isRequired,
                actor.UserId,
                cancellationToken);
            return "佐證版本已連結至治理對象並寫入存取紀錄。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostAcknowledgeAsync(
        Guid projectVersionId,
        string ruleCode,
        string explanation,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.EditInventory, async actor =>
        {
            await _governance.AcknowledgeReadinessRuleAsync(
                projectVersionId,
                ruleCode,
                explanation,
                actor.UserId,
                cancellationToken);
            return $"規則 {ruleCode} 的說明已保存。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostTransitionAsync(
        Guid projectVersionId,
        VerificationWorkflowState targetState,
        string reason,
        CancellationToken cancellationToken)
    {
        var actor = await _access.ResolveAsync(User, cancellationToken);
        if (actor is null)
        {
            return Forbid();
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
        if (!await IsAllowedAsync(permission))
        {
            return Forbid();
        }

        try
        {
            await _governance.TransitionAsync(
                projectVersionId,
                targetState,
                actor.UserId,
                actor.WorkflowRoles,
                actor.HasVerifiedMfa,
                reason,
                cancellationToken);
            StatusMessage = $"工作流程已轉換為 {targetState}。";
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException)
        {
            StatusMessage = $"操作失敗：{exception.Message}";
        }

        return RedirectToPage(new { projectVersionId });
    }

    public async Task<IActionResult> OnPostSaveFindingAsync(
        Guid projectVersionId,
        string findingJson,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.ReviewInventory, async actor =>
        {
            await _governance.SaveReviewFindingAsync(projectVersionId, findingJson, actor.UserId, cancellationToken);
            return "結構化查核發現已保存。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostSaveVerificationRecordAsync(
        Guid projectVersionId,
        string verificationRecordJson,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.VerifyInventory, async actor =>
        {
            await _governance.SaveVerificationRecordAsync(
                projectVersionId,
                verificationRecordJson,
                new WorkflowActor(
                    actor.UserId.ToString("D"),
                    _organizationScope.OrganizationId!.Value,
                    actor.WorkflowRoles,
                    actor.HasVerifiedMfa,
                    new HashSet<Guid>()),
                cancellationToken);
            return "第三方查驗紀錄與簽署摘要已保存。";
        }, cancellationToken);

    public async Task<IActionResult> OnPostGenerateArchiveAsync(
        Guid projectVersionId,
        Guid calculationRunId,
        CancellationToken cancellationToken)
    {
        var actor = await _access.ResolveAsync(User, cancellationToken);
        if (actor is null || !await IsAllowedAsync(OrganizationPermission.ReviewInventory))
        {
            return Forbid();
        }
        if (!actor.HasVerifiedMfa)
        {
            StatusMessage = "產生查驗封存檔前，必須以雙因素驗證完成目前登入。";
            return RedirectToPage(new { projectVersionId });
        }

        try
        {
            var archive = await _governance.GenerateVerificationArchiveAsync(
                projectVersionId,
                calculationRunId,
                actor.UserId,
                cancellationToken);
            StatusMessage = $"查驗封存檔已產生，SHA-256：{archive.ArchiveSha256}";
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException)
        {
            StatusMessage = $"操作失敗：{exception.Message}";
        }

        return RedirectToPage(new { projectVersionId });
    }

    public async Task<IActionResult> OnPostCompareRunsAsync(
        Guid projectVersionId,
        Guid previousRunId,
        Guid currentRunId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(projectVersionId, OrganizationPermission.ViewInventory, async actor =>
        {
            var comparison = await _governance.CompareRunsAsync(
                projectVersionId,
                previousRunId,
                currentRunId,
                actor.UserId,
                cancellationToken);
            return $"版本差異分析完成：{comparison.Changes.Count} 項變更。";
        }, cancellationToken);

    public async Task<IActionResult> OnGetArchiveAsync(Guid archiveId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ViewInventory))
        {
            return Forbid();
        }

        var archive = await _dbContext.VerificationArchives.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == archiveId, cancellationToken);
        return archive is null
            ? NotFound()
            : File(
                archive.ArchiveBytes,
                "application/zip",
                $"verification-archive-{archive.ProjectVersionId:N}-{archive.CalculationRunId:N}.zip");
    }

    public async Task<IActionResult> OnGetEvidenceAsync(Guid documentVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ViewInventory))
        {
            return Forbid();
        }

        var actor = await _access.ResolveAsync(User, cancellationToken);
        var version = await _dbContext.EvidenceDocumentVersions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == documentVersionId, cancellationToken);
        if (actor is null || version is null)
        {
            return NotFound();
        }

        var bytes = await _governance.DownloadEvidenceAsync(
            documentVersionId,
            actor.UserId,
            EvidenceRequestHash.Create(HttpContext.Connection.RemoteIpAddress?.ToString()),
            cancellationToken);
        return File(bytes, version.ContentType, version.OriginalFileName);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Projects = await _dbContext.InventoryProjectVersions.AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        ProjectVersionId ??= Projects.FirstOrDefault()?.Id;
        if (ProjectVersionId.HasValue && Projects.All(item => item.Id != ProjectVersionId.Value))
        {
            ProjectVersionId = Projects.FirstOrDefault()?.Id;
        }

        var actor = await _access.ResolveAsync(User, cancellationToken);
        HasVerifiedMfa = actor?.HasVerifiedMfa == true;
        CanEdit = await IsAllowedAsync(OrganizationPermission.EditInventory);
        CanManage = await IsAllowedAsync(OrganizationPermission.ManageGovernance);
        CanReview = await IsAllowedAsync(OrganizationPermission.ReviewInventory);
        CanVerify = await IsAllowedAsync(OrganizationPermission.VerifyInventory);
        Overview = await _governance.GetOverviewAsync(ProjectVersionId, cancellationToken);
        if (!ProjectVersionId.HasValue)
        {
            return;
        }

        Activities = await _dbContext.ActivityData.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == ProjectVersionId.Value)
            .OrderBy(item => item.LifecycleStage)
            .ThenBy(item => item.Name)
            .ToArrayAsync(cancellationToken);
        Runs = await _dbContext.CalculationRuns.AsNoTracking()
            .Where(item => item.ProjectVersionId == ProjectVersionId.Value)
            .OrderByDescending(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        AcknowledgedRuleCodes = await _governance.GetAcknowledgedRuleCodesAsync(
            ProjectVersionId.Value,
            cancellationToken);
        Readiness = await _governance.BuildReadinessReportAsync(
            ProjectVersionId.Value,
            AcknowledgedRuleCodes,
            actor?.UserId,
            persist: false,
            cancellationToken);
    }

    private async Task<IActionResult> ExecuteAsync(
        Guid? projectVersionId,
        OrganizationPermission permission,
        Func<GovernanceActorContext, Task<string>> action,
        CancellationToken cancellationToken)
    {
        ProjectVersionId = projectVersionId;
        if (!await IsAllowedAsync(permission))
        {
            return Forbid();
        }

        var actor = await _access.ResolveAsync(User, cancellationToken);
        if (actor is null)
        {
            return Forbid();
        }

        try
        {
            StatusMessage = await action(actor);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException)
        {
            StatusMessage = $"操作失敗：{exception.Message}";
        }

        return RedirectToPage(new { projectVersionId });
    }

    private async Task<bool> IsAllowedAsync(OrganizationPermission permission) =>
        (await _authorization.AuthorizeAsync(
            User,
            resource: null,
            new OrganizationPermissionRequirement(permission))).Succeeded;
}
