using System.Text.Json;
using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Application.Exports;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

/// <summary>Read-only Excel export of an inventory; it never writes to the database.</summary>
public sealed class InventoryExportService(
    CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IInventorySnapshotReader snapshotReader,
    CalculationBuildProvenance buildProvenance) : IInventoryExport
{
    public async Task<ExportOutcome> ExportExcelAsync(
        OperationContext context, Guid projectVersionId, Guid? runId, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ViewInventory, requireMfa: true))
        {
            return new ExportOutcome(OperationStatus.Forbidden);
        }
        var project = await db.InventoryProjectVersions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == projectVersionId, cancellationToken);
        if (project is null)
        {
            return new ExportOutcome(OperationStatus.NotFound);
        }
        var runQuery = db.CalculationRuns.AsNoTracking()
            .Where(item => item.ProjectVersionId == project.Id);
        if (runId.HasValue)
        {
            runQuery = runQuery.Where(item => item.Id == runId.Value);
        }
        var run = await runQuery.OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (runId.HasValue && run is null)
        {
            return new ExportOutcome(OperationStatus.NotFound);
        }
        if (run is not null && !CalculationRunIntegrity.TryReadRules(run, out _))
        {
            return new ExportOutcome(OperationStatus.Conflict);
        }
        var manifest = run is not null
            ? (Json: run.CanonicalInputManifest, Sha256: run.InputSha256)
            : CanonicalManifest.Create(await snapshotReader.ReadAsync(project.Id, cancellationToken), buildProvenance);
        var references = CanonicalManifest.ReadEvidenceReferences(manifest.Json, manifest.Sha256);
        using var document = JsonDocument.Parse(manifest.Json);
        var root = document.RootElement;
        var activities = root.GetProperty("activities").EnumerateArray().ToArray();
        var factorIds = activities.Select(activity => activity.GetProperty("factorVersionId").GetGuid()).Distinct().ToArray();
        var factorMetadata = await db.EmissionFactorVersions.AsNoTracking()
            .Where(factor => factorIds.Contains(factor.Id)).ToDictionaryAsync(factor => factor.Id, cancellationToken);
        var lines = run is null ? [] : await db.CalculationLineItems.AsNoTracking()
            .Where(item => item.CalculationRunId == run.Id)
            .OrderBy(item => item.LifecycleStage).ThenBy(item => item.ActivityId).ToArrayAsync(cancellationToken);
        var evidenceActivityIds = references.Keys.ToArray();
        var evidenceFiles = await db.EvidenceFiles.AsNoTracking()
            .Where(item => evidenceActivityIds.Contains(item.ActivityDataId))
            .OrderBy(item => item.ActivityDataId).ToArrayAsync(cancellationToken);
        var summaryRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "欄位", "內容" },
            new object?[] { "功能單位", root.GetProperty("functionalUnit").GetString() },
            new object?[] { "宣告單位", root.GetProperty("declaredUnit").GetString() },
            new object?[] { "盤查期間", $"{root.GetProperty("periodStart").GetString()}～{root.GetProperty("periodEnd").GetString()}" },
            new object?[] { "系統邊界", root.GetProperty("systemBoundary").GetString() },
            new object?[] { "分配方法", root.GetProperty("allocationMethod").GetString() },
            new object?[] { "PCR 版本", root.GetProperty("pcrVersion").GetString() },
            new object?[] { "資料範圍", run is null ? "尚未建立計算版本；匯出目前活動資料" : $"計算版本 {run.Id:N} 的不可變輸入與結果" },
            new object?[] { "輸入 SHA-256", manifest.Sha256 },
            new object?[] { "係數來源欄位", "來源與名稱參照所引用的係數版本目錄；活動量、係數值、單位與分配比例取自本次匯出快照。" },
            new object?[] { "計算結果", run?.ProductTotal },
            new object?[] { "結果單位", run is null ? string.Empty : "kgCO2e" }
        };
        var activityRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "階段", "活動名稱", "活動類型", "原始活動量", "原始單位", "標準活動量", "標準單位",
                "係數名稱", "係數版本", "係數值", "係數單位", "分配比例", "資料品質", "來源", "活動 ID" }
        };
        activityRows.AddRange(activities.Select(activity =>
        {
            factorMetadata.TryGetValue(activity.GetProperty("factorVersionId").GetGuid(), out var metadata);
            return (IReadOnlyList<object?>)new object?[]
            {
                InventoryDisplayNames.Stage(Enum.Parse<LifecycleStage>(activity.GetProperty("stage").GetString()!)),
                activity.GetProperty("name").GetString(),
                InventoryDisplayNames.ActivityKind(Enum.Parse<ActivityDataKind>(activity.GetProperty("kind").GetString()!)),
                activity.GetProperty("rawValue").GetDecimal(), activity.GetProperty("rawUnitCode").GetString(),
                activity.GetProperty("canonicalValue").GetDecimal(), activity.GetProperty("canonicalUnitCode").GetString(),
                metadata?.Name ?? "係數版本目錄資料遺失", metadata?.VersionNumber,
                activity.GetProperty("factorValue").GetDecimal(),
                $"{activity.GetProperty("factorNumeratorUnit").GetString()}/{activity.GetProperty("factorDenominatorUnit").GetString()}",
                activity.GetProperty("allocationFactor").GetDecimal(), activity.GetProperty("dataQuality").GetString(),
                activity.GetProperty("sourceReference").GetString(), activity.GetProperty("id").GetGuid()
            };
        }));
        var factorRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "係數名稱", "版本", "係數值", "單位", "地域", "來源機構", "來源資料集",
                "公告版本", "來源網址", "原始資料 SHA-256", "係數版本 ID" }
        };
        factorRows.AddRange(activities.DistinctBy(activity => activity.GetProperty("factorVersionId").GetGuid())
            .Select(activity =>
            {
                var id = activity.GetProperty("factorVersionId").GetGuid();
                factorMetadata.TryGetValue(id, out var metadata);
                return (IReadOnlyList<object?>)new object?[]
                {
                    metadata?.Name ?? "係數版本目錄資料遺失", metadata?.VersionNumber,
                    activity.GetProperty("factorValue").GetDecimal(),
                    $"{activity.GetProperty("factorNumeratorUnit").GetString()}/{activity.GetProperty("factorDenominatorUnit").GetString()}",
                    metadata is null ? null : InventoryDisplayNames.Geography(metadata.Geography), metadata?.SourceName,
                    metadata?.DatasetName, metadata?.SourceDatasetVersion, metadata?.SourceReference,
                    metadata?.OriginalDocumentSha256, id
                };
            }));
        var resultRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "階段", "活動 ID", "標準活動量", "單位", "係數值", "係數單位", "排放量", "結果單位" }
        };
        resultRows.AddRange(lines.Select(line => (IReadOnlyList<object?>)new object?[]
        {
            InventoryDisplayNames.Stage((LifecycleStage)line.LifecycleStage), line.ActivityId,
            line.CanonicalActivityValue, line.ActivityUnitCode, line.FactorValue, line.FactorUnit,
            line.Emissions, line.EmissionsUnitCode
        }));
        var evidenceRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "活動 ID", "檔案名稱", "SHA-256", "掃描狀態" }
        };
        evidenceRows.AddRange(evidenceFiles.Where(item => string.Equals(references[item.ActivityDataId], item.Sha256, StringComparison.OrdinalIgnoreCase))
            .Select(item => (IReadOnlyList<object?>)new object?[] { item.ActivityDataId, item.OriginalFileName, item.Sha256, item.ScanStatus }));
        var workbook = ExcelWorkbook.Create(
        [
            new ExcelSheet("盤查摘要", summaryRows), new ExcelSheet("五階段活動", activityRows),
            new ExcelSheet("使用係數", factorRows), new ExcelSheet("計算結果", resultRows),
            new ExcelSheet("佐證索引", evidenceRows)
        ]);
        return new ExportOutcome(OperationStatus.Succeeded, new ExportFile(workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"碳足跡盤查-{(run?.Id ?? project.Id):N}.xlsx"));
    }
}
