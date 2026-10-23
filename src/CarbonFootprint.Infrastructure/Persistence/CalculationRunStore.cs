using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Domain.Modules.Calculations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace CarbonFootprint.Infrastructure.Persistence;

public sealed class CalculationRunStore : ICalculationRunStore
{
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IHttpContextAccessor? _http;

    public CalculationRunStore(CarbonFootprintDbContext dbContext, IHttpContextAccessor? http = null)
    {
        _dbContext = dbContext;
        _http = http;
    }

    public async Task SaveAsync(CalculationRun run, CancellationToken cancellationToken)
    {
        if (await _dbContext.CalculationRuns.AnyAsync(item => item.Id == run.Id, cancellationToken))
        {
            throw new InvalidOperationException("CalculationRun 已存在，禁止覆寫。");
        }

        var record = new CalculationRunRecord
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
            DataQualitySummaryJson = JsonSerializer.Serialize(run.DataQualitySummary),
            ProductTotal = run.ProductTotal,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.CalculationRuns.Add(record);
        _dbContext.CalculationLineItems.AddRange(run.LineItems.Select(line => new CalculationLineRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = run.OrganizationId,
            CalculationRunId = run.Id,
            ActivityId = line.ActivityId,
            LifecycleStage = (int)line.Stage,
            FormulaId = line.FormulaId,
            ActivityAmountFormulaId = line.ActivityAmountFormulaId,
            FormulaInputsJson = line.FormulaInputsJson,
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
        var actor = Guid.TryParse(_http?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) ? actorId : (Guid?)null;
        new AuditWriter(_dbContext).AppendTracked("calculation.run.created", _dbContext.Entry(record), actor,
            "Explicit immutable calculation run creation", _http?.HttpContext?.TraceIdentifier ?? run.Id.ToString("N"),
            AuditWriter.RequestSessionReference(_http?.HttpContext),
            metadata: new Dictionary<string, object?> { ["inputManifestSha256"] = run.InputSha256, ["lineCount"] = run.LineItems.Count });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
