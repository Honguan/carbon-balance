using CarbonFootprint.Application.Inventories;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class InventoryProjectService(CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IOrganizationScope scope) : IInventoryProjects
{
    private const string StageResource = "LifecycleStageDeclaration";
    private Guid OrganizationId => scope.OrganizationId ?? throw new InvalidOperationException("缺少目前組織範圍。");

    public async Task<OperationResult> CreateAsync(OperationContext context, InventoryInput input, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.EditInventory, requireMfa: false)) return OperationResult.Forbidden;
        if (input.PeriodStart > input.PeriodEnd
            || string.IsNullOrWhiteSpace(input.FunctionalUnit)
            || string.IsNullOrWhiteSpace(input.DeclaredUnit)
            || string.IsNullOrWhiteSpace(input.SystemBoundary)
            || string.IsNullOrWhiteSpace(input.AllocationMethod)
            || string.IsNullOrWhiteSpace(input.AllocationReason))
        {
            return OperationResult.Invalid("inventory", "請提供有效期間、功能單位與 PCR 版本識別。");
        }
        if (input.ProductVersionId == Guid.Empty || input.PcrVersionId == Guid.Empty)
        {
            return OperationResult.Invalid("inventory", "請先建立並選擇產品版本與已發布 PCR 版本。");
        }

        var organizationId = OrganizationId;
        var productContext = await (
                from version in db.ProductVersions.AsNoTracking()
                join product in db.Products.AsNoTracking() on version.ProductId equals product.Id
                where version.Id == input.ProductVersionId && version.OrganizationId == organizationId
                select new { version.Id, product.CategoryCode })
            .SingleOrDefaultAsync(cancellationToken);
        if (productContext is null) return OperationResult.Invalid("productVersionId", "所選產品版本不存在或不屬於目前組織。");

        var pcr = await db.PcrVersions.SingleOrDefaultAsync(
            item => item.Id == input.PcrVersionId && item.OrganizationId == organizationId, cancellationToken);
        if (pcr is null) return OperationResult.Invalid("pcrVersionId", "所選 PCR 版本不存在或不屬於目前組織。");

        var pcrStageRules = await db.PcrStageRules.AsNoTracking()
            .Where(item => item.PcrVersionId == pcr.Id).OrderBy(item => item.LifecycleStage).ToArrayAsync(cancellationToken);
        var pcrRuleSet = PcrRuleSetMapper.ToRuleSet(pcr, pcrStageRules);
        var defaultStageApplicability = Enum.GetValues<LifecycleStage>().ToDictionary(
            stage => stage,
            stage => pcrRuleSet.StageRules.SingleOrDefault(item => item.Stage == stage)?.Requirement != PcrStageRequirement.Prohibited);
        var violations = PcrRuleEngine.Validate(
            pcrRuleSet,
            new PcrProjectContext(
                Guid.Empty, productContext.CategoryCode, input.PeriodEnd, input.FunctionalUnit.Trim(), input.DeclaredUnit.Trim(),
                input.SystemBoundary.Trim(), input.AllocationMethod.Trim(), defaultStageApplicability, [], input.Exclusions?.Trim() ?? string.Empty),
            requireCompleteInventory: false);
        if (violations.Count > 0)
        {
            return OperationResult.Invalid("inventory", violations.Select(violation => $"{violation.Code}：{violation.Message}").ToArray());
        }

        var nextVersion = (await db.InventoryProjectVersions
            .Where(item => item.ProductVersionId == input.ProductVersionId)
            .MaxAsync(item => (int?)item.VersionNumber, cancellationToken) ?? 0) + 1;
        var projectId = Guid.NewGuid();
        db.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
        {
            Id = projectId,
            OrganizationId = organizationId,
            ProductVersionId = input.ProductVersionId,
            VersionNumber = nextVersion,
            PeriodStart = input.PeriodStart,
            PeriodEnd = input.PeriodEnd,
            FunctionalUnit = input.FunctionalUnit.Trim(),
            DeclaredUnit = input.DeclaredUnit.Trim(),
            SystemBoundary = input.SystemBoundary.Trim(),
            AllocationMethod = input.AllocationMethod.Trim(),
            AllocationReason = input.AllocationReason.Trim(),
            Exclusions = input.Exclusions?.Trim() ?? string.Empty,
            Assumptions = input.Assumptions?.Trim() ?? string.Empty,
            EstimationReason = input.EstimationReason?.Trim() ?? string.Empty,
            PcrVersionId = pcr.Id,
            PcrVersion = $"{pcr.RegistrationNumber}-v{pcr.VersionNumber}",
            WorkflowStatus = InventoryWorkflowStatus.Draft.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.LifecycleStageDeclarations.AddRange(Enum.GetValues<LifecycleStage>().Select(stage => new LifecycleStageDeclarationRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            InventoryProjectVersionId = projectId,
            LifecycleStage = (int)stage,
            IsApplicable = defaultStageApplicability[stage],
            Reason = defaultStageApplicability[stage] ? string.Empty : "PCR 規則禁止納入此階段。"
        }));
        OperationAudit.AppendTracked(db, context, organizationId, "inventory.version.created", "InventoryProjectVersion", projectId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_inventory_project_versions_product_version_id_version_number"
        })
        {
            db.ChangeTracker.Clear();
            return OperationResult.Invalid("inventory", "另一位使用者剛建立了此產品的盤查版本，請重新儲存以建立下一版。");
        }
        return OperationResult.Success($"盤查專案第 {nextVersion} 版已建立。", projectId);
    }

    public async Task<OperationResult> SetStageApplicabilityAsync(
        OperationContext context, Guid stageDeclarationId, bool isApplicable, string? reason, uint rowVersion, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.EditInventory, requireMfa: false)) return OperationResult.Forbidden;
        var declaration = await db.LifecycleStageDeclarations.SingleOrDefaultAsync(item => item.Id == stageDeclarationId, cancellationToken);
        if (declaration is null) return OperationResult.NotFound;
        if (rowVersion != declaration.RowVersion) return await StaleAsync(context, declaration.Id, cancellationToken);
        var project = await db.InventoryProjectVersions.SingleAsync(item => item.Id == declaration.InventoryProjectVersionId, cancellationToken);
        if (!InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus))) return OperationResult.BadRequest;

        var pcrStageRule = project.PcrVersionId.HasValue
            ? await db.PcrStageRules.AsNoTracking().SingleOrDefaultAsync(
                item => item.PcrVersionId == project.PcrVersionId.Value && item.LifecycleStage == declaration.LifecycleStage, cancellationToken)
            : null;
        if (pcrStageRule is not null
            && Enum.TryParse<PcrStageRequirement>(pcrStageRule.Requirement, out var requirement)
            && ((requirement == PcrStageRequirement.Mandatory && !isApplicable) || (requirement == PcrStageRequirement.Prohibited && isApplicable)))
        {
            return OperationResult.Invalid("stage", "階段適用性不可違反已選 PCR 的必要或禁止規則。");
        }
        if (!isApplicable && string.IsNullOrWhiteSpace(reason)) return OperationResult.Invalid("stage", "不適用階段必須填寫原因。");
        if (!isApplicable && await db.ActivityData.AnyAsync(
                item => item.InventoryProjectVersionId == project.Id && item.LifecycleStage == declaration.LifecycleStage && item.RetiredAt == null,
                cancellationToken))
        {
            return OperationResult.Invalid("stage", "已有活動數據的階段不可標記為不適用。");
        }

        declaration.IsApplicable = isApplicable;
        declaration.Reason = isApplicable ? string.Empty : reason!.Trim();
        OperationAudit.AppendTracked(db, context, OrganizationId, "inventory.stage.applicability.changed", StageResource, declaration.Id);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await StaleAsync(context, declaration.Id, cancellationToken);
        }
        return OperationResult.Success(projectVersionId: project.Id);
    }

    private async Task<OperationResult> StaleAsync(OperationContext context, Guid declarationId, CancellationToken cancellationToken)
    {
        await OperationAudit.RecordStaleRejectionAsync(db, context, OrganizationId, "SetStageApplicability", StageResource, declarationId, cancellationToken);
        return OperationResult.Conflict;
    }
}
