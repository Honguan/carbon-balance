using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class InventoryCalculationService(
    CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IInventoryReadiness readiness,
    IInventorySnapshotReader snapshotReader, CalculateInventoryHandler handler, CalculationBuildProvenance buildProvenance) : IInventoryCalculation
{
    public async Task<OperationResult> CalculateAsync(OperationContext context, Guid projectVersionId, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.CreateCalculationRun, requireMfa: true)) return OperationResult.Forbidden;
        var project = await db.InventoryProjectVersions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == projectVersionId, cancellationToken);
        if (project is null) return OperationResult.NotFound;
        if (!InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus)))
        {
            return OperationResult.Invalid("calculation", "盤查已送審或核准，不可建立新計算版本。");
        }
        if (!project.PcrVersionId.HasValue) return OperationResult.Invalid("calculation", "盤查版本未綁定受治理的 PCR 版本。");

        var violations = await readiness.ValidatePcrAsync(project.Id, requireCompleteInventory: true, cancellationToken);
        if (violations.Count > 0)
        {
            return OperationResult.Invalid("calculation", violations.Select(violation => $"{violation.Code}：{violation.Message}").ToArray());
        }

        try
        {
            var snapshot = await snapshotReader.ReadAsync(project.Id, cancellationToken);
            var supersedesRunId = await db.CalculationRuns
                .Where(item => item.ProjectVersionId == project.Id)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            // The run store appends the calculation.run.created audit in the same save.
            await handler.HandleAsync(new CalculateInventoryCommand(Guid.NewGuid(), snapshot, buildProvenance, supersedesRunId), cancellationToken);
            return OperationResult.Success("不可變計算版本已建立。");
        }
        catch (InvalidOperationException exception)
        {
            return OperationResult.Invalid("calculation", exception.Message);
        }
    }
}
