using CarbonFootprint.Application.Factors;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Persistence;

namespace CarbonFootprint.Web.Services;

/// <summary>Authorized operation around <see cref="MoenvFactorSynchronizationService"/>, which writes its own audit evidence.</summary>
public sealed class MoenvFactorSyncOperation(
    MoenvFactorSynchronizationService synchronization, IOperationAuthorizer authorizer, IOrganizationScope scope) : IFactorCatalogueSync
{
    public async Task<OperationResult> SynchronizeAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: true)) return OperationResult.Forbidden;
        var organizationId = scope.OrganizationId ?? throw new InvalidOperationException("缺少目前組織範圍。");
        try
        {
            var result = await synchronization.SynchronizeOrganizationAsync(organizationId, context.ActorId, context.CorrelationId, cancellationToken);
            return OperationResult.Success(
                $"環境部係數同步完成：新增並發布 {result.CreatedCount} 筆、啟用舊草稿 {result.PublishedExistingCount} 筆、未變更 {result.UnchangedCount} 筆、略過 {result.SkippedCount} 筆無法對應的資料。");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return OperationResult.Invalid("factor", $"環境部係數同步失敗：{exception.Message}");
        }
    }
}
