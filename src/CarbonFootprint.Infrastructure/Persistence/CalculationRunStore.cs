using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Domain.Modules.Calculations;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Persistence;

public sealed class CalculationRunStore : ICalculationRunStore
{
    private readonly CarbonFootprintDbContext _dbContext;

    public CalculationRunStore(CarbonFootprintDbContext dbContext) => _dbContext = dbContext;

    public async Task SaveAsync(CalculationRun run, CancellationToken cancellationToken)
    {
        if (await _dbContext.CalculationRuns.AnyAsync(item => item.Id == run.Id, cancellationToken))
        {
            throw new InvalidOperationException("CalculationRun 已存在，禁止覆寫。");
        }

        _dbContext.CalculationRuns.Add(new CalculationRunRecord
        {
            Id = run.Id,
            OrganizationId = run.OrganizationId,
            ProjectVersionId = run.ProjectVersionId,
            SupersedesRunId = run.SupersedesRunId,
            CanonicalInputManifest = run.CanonicalInputManifest,
            InputSha256 = run.InputSha256,
            EngineBuild = run.EngineBuild,
            RuleSetVersion = run.RuleSetVersion,
            UnitCatalogueVersion = run.UnitCatalogueVersion,
            GwpVersion = run.GwpVersion,
            PcrVersion = run.PcrVersion,
            ProductTotal = run.ProductTotal,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _dbContext.CalculationLineItems.AddRange(run.LineItems.Select(line => new CalculationLineRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = run.OrganizationId,
            CalculationRunId = run.Id,
            ActivityId = line.ActivityId,
            LifecycleStage = (int)line.Stage,
            FormulaId = line.FormulaId,
            CanonicalActivityValue = line.CanonicalActivityValue,
            ActivityUnitCode = line.ActivityUnitCode,
            FactorVersionId = line.FactorVersionId,
            FactorValue = line.FactorValue,
            FactorUnit = line.FactorUnit,
            AllocationFactor = line.AllocationFactor,
            Emissions = line.Emissions,
            EmissionsUnitCode = line.EmissionsUnitCode
        }));
        _dbContext.CalculationStageSummaries.AddRange(run.StageSummaries.Select(summary => new CalculationStageSummaryRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = run.OrganizationId,
            CalculationRunId = run.Id,
            LifecycleStage = (int)summary.Stage,
            Emissions = summary.Emissions
        }));
        _dbContext.CalculationWarnings.AddRange(run.Warnings.Select(warning => new CalculationWarningRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = run.OrganizationId,
            CalculationRunId = run.Id,
            Code = warning.Code,
            Message = warning.Message
        }));
        _dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = null,
            OrganizationId = run.OrganizationId,
            Action = "calculation.run.created",
            ResourceType = "CalculationRun",
            ResourceId = run.Id,
            BeforeHash = null,
            AfterHash = run.InputSha256,
            CorrelationId = run.Id.ToString("N"),
            MetadataJson = "{}"
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
