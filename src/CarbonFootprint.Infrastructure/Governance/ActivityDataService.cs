using System.Text.Json;
using CarbonFootprint.Application.Inventories;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Units;
using CarbonFootprint.Infrastructure.Evidence;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class ActivityDataService(
    CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IOrganizationScope scope, EvidenceStorageService evidenceStorage) : IActivityData
{
    private const string ActivityResource = "ActivityDataVersion";
    private Guid OrganizationId => scope.OrganizationId ?? throw new InvalidOperationException("缺少目前組織範圍。");

    public async Task<OperationResult> SaveAsync(
        OperationContext context, ActivityInput input, Guid? supersedesActivityId, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.EditInventory, requireMfa: false)) return OperationResult.Forbidden;

        var organizationId = OrganizationId;
        var existing = supersedesActivityId.HasValue
            ? await db.ActivityData.SingleOrDefaultAsync(
                item => item.Id == supersedesActivityId.Value && item.InventoryProjectVersionId == input.InventoryProjectVersionId
                    && item.RetiredAt == null, cancellationToken)
            : null;
        if (supersedesActivityId.HasValue && existing is null) return OperationResult.NotFound;
        if (input.InventoryProjectVersionId == Guid.Empty || input.FactorVersionId == Guid.Empty)
        {
            return Invalid("請先建立盤查版本並選擇已發布排放係數。");
        }

        var project = await db.InventoryProjectVersions.SingleOrDefaultAsync(
            item => item.Id == input.InventoryProjectVersionId && item.OrganizationId == organizationId, cancellationToken);
        var factor = await db.EmissionFactorVersions.SingleOrDefaultAsync(
            item => item.Id == input.FactorVersionId && item.OrganizationId == organizationId
                && item.PublicationStatus == FactorPublicationStatus.Published.ToString(),
            cancellationToken);
        if (project is null || factor is null) return Invalid("所選盤查版本或排放係數不存在於目前組織。");
        if (!AllowsEditing(project)) return Invalid("盤查已送審或核准，不可再新增活動資料。");

        var stageDeclaration = await db.LifecycleStageDeclarations.SingleOrDefaultAsync(
            item => item.InventoryProjectVersionId == project.Id && item.LifecycleStage == (int)input.LifecycleStage, cancellationToken);
        if (stageDeclaration is null || !stageDeclaration.IsApplicable) return Invalid("不適用階段不可新增活動數據。");

        var factorVersion = new EmissionFactorVersion(
            factor.Id, factor.FactorId, factor.VersionNumber, factor.Name, factor.Value, factor.NumeratorUnitCode,
            factor.DenominatorUnitCode, factor.Geography, factor.ValidFrom, factor.ValidTo,
            Enum.Parse<FactorPublicationStatus>(factor.PublicationStatus), factor.SourceDatasetVersion, factor.LicenseCode,
            Enum.Parse<FactorReviewStatus>(factor.ReviewStatus), factor.Applicability);
        if (!factorVersion.IsSelectableOn(project.PeriodEnd)) return Invalid("係數版本未發布、已撤回或不在盤查期間有效範圍。");

        var hasName = TryResolveControlledValue(input.ActivityName, input.ActivityNameOther, out var name);
        var hasEquipment = TryResolveOptionalControlledValue(input.EquipmentCategory, input.EquipmentCategoryOther, out var equipment);
        var hasSource = TryResolveControlledValue(input.DataSourceType, input.DataSourceTypeOther, out var sourceType);
        var hasProvider = TryResolveControlledValue(input.DataProviderType, input.DataProviderOther, out var provider);
        var hasMethod = TryResolveControlledValue(input.CollectionMethod, input.CollectionMethodOther, out var method);
        if (!hasName || !hasEquipment || !hasSource || !hasProvider || !hasMethod
            || string.IsNullOrWhiteSpace(input.SourceReference)
            || !ActivityKindRules.IsAllowed(input.LifecycleStage, input.ActivityKind)
            || input.AllocationFactor is null or <= 0m or > 1m
            || string.IsNullOrWhiteSpace(input.DataQuality)
            || (input.IsEstimated && string.IsNullOrWhiteSpace(input.EstimationReason)))
        {
            return Invalid("活動項目、來源類型、提供者、取得方式與來源參照皆為必填。");
        }

        try
        {
            var unitCatalogueVersion = UnitCatalogue.ResolveVersion(await db.ActivityData
                .Where(item => item.InventoryProjectVersionId == project.Id && item.RetiredAt == null)
                .Select(item => item.ConversionRuleVersion).Distinct().ToArrayAsync(cancellationToken));
            var derived = ActivityAmountFormula.Derive(
                input.ActivityKind, input.RawValue, input.RawUnitCode!, input.TransportDistanceKm, input.TransportWeightKg,
                input.UseLifetime, input.UseFrequency, input.UseConsumptionPerUse);
            var canonicalUnitCode = ActivityAmountFormula.IsTransport(input.ActivityKind) ? derived.UnitCode : input.CanonicalUnitCode!;
            var unitRecords = await db.Units
                .Where(item => item.CatalogueVersion == unitCatalogueVersion
                    && (item.Code == derived.UnitCode || item.Code == canonicalUnitCode))
                .ToArrayAsync(cancellationToken);
            var catalogue = new UnitCatalogue(unitCatalogueVersion, unitRecords.Select(item => new UnitDefinition(
                item.Id, item.Code, item.Dimension, item.ScaleToCanonical, item.OffsetToCanonical, item.CanonicalCode, item.CatalogueVersion,
                item.AliasesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                string.IsNullOrWhiteSpace(item.CompositeExpression) ? null : item.CompositeExpression)));
            var canonicalValue = catalogue.Convert(derived.Value, derived.UnitCode, canonicalUnitCode);
            if (!string.Equals(canonicalUnitCode, factor.DenominatorUnitCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("活動標準單位必須等於係數分母單位。");
            }

            var newActivityId = Guid.NewGuid();
            if (existing is not null)
            {
                existing.RetiredAt = DateTimeOffset.UtcNow;
                OperationAudit.AppendTracked(db, context, organizationId, "activity.version.superseded", ActivityResource, existing.Id);
            }
            db.ActivityData.Add(new ActivityDataRecord
            {
                Id = newActivityId,
                OrganizationId = organizationId,
                InventoryProjectVersionId = project.Id,
                LifecycleStage = (int)input.LifecycleStage,
                Name = name,
                ActivityKind = input.ActivityKind.ToString(),
                SupplierOrScenario = string.Join("｜", new[] { input.SupplierOrScenario?.Trim(), $"計算基礎：{derived.FormulaTrace}" }
                    .Where(item => !string.IsNullOrWhiteSpace(item))),
                EquipmentCategory = equipment,
                DataSourceType = sourceType,
                DataProvider = provider,
                CollectionMethod = method,
                SourceReference = input.SourceReference!.Trim(),
                RawValue = derived.Value,
                RawUnitCode = derived.UnitCode,
                CanonicalValue = canonicalValue,
                CanonicalUnitCode = canonicalUnitCode,
                ConversionRuleVersion = unitCatalogueVersion,
                AmountFormulaId = derived.FormulaId,
                FormulaInputsJson = JsonSerializer.Serialize(derived.Inputs),
                PeriodStart = project.PeriodStart,
                PeriodEnd = project.PeriodEnd,
                FactorVersionId = factor.Id,
                AllocationFactor = input.AllocationFactor.Value,
                IsEstimated = input.IsEstimated,
                EstimationReason = input.EstimationReason?.Trim() ?? string.Empty,
                DataQuality = input.DataQuality!.Trim(),
                EvidenceSha256 = null
            });
            OperationAudit.AppendTracked(db, context, organizationId, "activity.version.created", ActivityResource, newActivityId);
            await db.SaveChangesAsync(cancellationToken);
            return OperationResult.Success(
                existing is null ? "活動數據已保存。" : "活動數據已更正；舊資料與佐證保留，新版本請重新附上適用佐證。", project.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Invalid("此活動已由另一個操作更正或移除，請重新載入後再操作。");
        }
        catch (InvalidOperationException exception)
        {
            return Invalid(exception.Message);
        }
    }

    public async Task<OperationResult> RetireAsync(OperationContext context, Guid activityId, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.EditInventory, requireMfa: false)) return OperationResult.Forbidden;
        var activity = await db.ActivityData.SingleOrDefaultAsync(item => item.Id == activityId && item.RetiredAt == null, cancellationToken);
        if (activity is null) return OperationResult.NotFound;
        var project = await db.InventoryProjectVersions.SingleAsync(item => item.Id == activity.InventoryProjectVersionId, cancellationToken);
        if (!AllowsEditing(project)) return OperationResult.BadRequest;

        activity.RetiredAt = DateTimeOffset.UtcNow;
        OperationAudit.AppendTracked(db, context, OrganizationId, "activity.retired", ActivityResource, activity.Id);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return OperationResult.Conflict;
        }
        return OperationResult.Success("活動已從目前盤查移除；歷史資料與佐證仍保留。", project.Id);
    }

    public async Task<OperationResult> AttachEvidenceAsync(
        OperationContext context, Guid activityId, EvidenceUpload upload, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.EditInventory, requireMfa: false)) return OperationResult.Forbidden;
        var activity = await db.ActivityData.SingleOrDefaultAsync(item => item.Id == activityId && item.RetiredAt == null, cancellationToken);
        if (activity is null) return OperationResult.NotFound;
        var project = await db.InventoryProjectVersions.SingleAsync(item => item.Id == activity.InventoryProjectVersionId, cancellationToken);
        if (!AllowsEditing(project)) return OperationResult.Invalid("evidence", "盤查已送審或核准，不可再變更佐證檔案。");
        if (upload.Content is null || upload.Length <= 0) return OperationResult.Invalid("evidence", "請選擇非空白佐證檔案。");
        if (await db.EvidenceFiles.AnyAsync(item => item.ActivityDataId == activity.Id, cancellationToken))
        {
            return OperationResult.Invalid("evidence", "每筆活動僅允許一份佐證檔案；請建立活動資料新版本以更換。");
        }

        try
        {
            var stored = await evidenceStorage.StoreAsync(OrganizationId, upload.Content, upload.FileName, upload.ContentType, cancellationToken);
            db.EvidenceFiles.Add(new EvidenceFileRecord
            {
                Id = stored.Id,
                OrganizationId = OrganizationId,
                ActivityDataId = activity.Id,
                ObjectKey = stored.ObjectKey,
                OriginalFileName = stored.OriginalFileName,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                Sha256 = stored.Sha256,
                ScanStatus = stored.ScanStatus.ToString(),
                CreatedAt = DateTimeOffset.UtcNow
            });
            activity.EvidenceSha256 = stored.Sha256;
            OperationAudit.AppendTracked(db, context, OrganizationId, "evidence.uploaded", "EvidenceFile", stored.Id);
            await db.SaveChangesAsync(cancellationToken);
            return OperationResult.Success("佐證檔案已通過惡意程式掃描、寫入物件儲存並綁定活動。", activity.InventoryProjectVersionId);
        }
        catch (EvidenceUploadException exception)
        {
            return OperationResult.Invalid("evidence", $"佐證檔案未保存：{exception.Message}");
        }
    }

    private static OperationResult Invalid(string message) => OperationResult.Invalid("activity", message);

    private static bool AllowsEditing(InventoryProjectVersionRecord project) =>
        InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus));

    private static bool TryResolveControlledValue(string? selected, string? other, out string value)
    {
        value = selected?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!string.Equals(value, "__other__", StringComparison.Ordinal)) return true;
        value = other?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryResolveOptionalControlledValue(string? selected, string? other, out string value)
    {
        value = string.Empty;
        return string.IsNullOrWhiteSpace(selected) || TryResolveControlledValue(selected, other, out value);
    }
}
