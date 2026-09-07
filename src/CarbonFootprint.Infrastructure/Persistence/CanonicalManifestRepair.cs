using CarbonFootprint.Domain.Modules.Calculations;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Persistence;

/// <summary>Explicit offline recovery of JSONB formatting damage; never called by a web request.</summary>
public static class CanonicalManifestRepair
{
    public static async Task<int> RepairAsync(CarbonFootprintDbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // ponytail: offline repair holds all manifests in memory; batch with a durable preflight ledger if volume requires it.
        var runs = await db.CalculationRuns.FromSqlRaw("SELECT * FROM app.calculation_runs FOR UPDATE")
            .IgnoreQueryFilters().AsNoTracking().ToArrayAsync(cancellationToken);
        var repairs = new List<(CalculationRunRecord Run, string Original)>();
        foreach (var run in runs)
        {
            if (CanonicalManifest.HasValidSha256(run.CanonicalInputManifest, run.InputSha256)) continue;
            if (!LegacyManifestRecovery.TryRecover(run.CanonicalInputManifest, run.InputSha256, out var original))
            {
                throw new InvalidOperationException($"計算 {run.Id} 無法還原為原始 SHA-256；未修復任何資料，請從可信備份復原。");
            }
            repairs.Add((run, original));
        }

        var correlationId = Guid.NewGuid().ToString("N");
        const string metadata = "{\"reason\":\"legacy-jsonb-formatting\",\"originalHashPreserved\":true}";
        foreach (var (run, original) in repairs)
        {
            // Restore the exact originally hashed bytes without changing the calculation or its hash.
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE app.calculation_runs SET canonical_input_manifest = {original}
                WHERE id = {run.Id} AND input_sha256 = {run.InputSha256}
                    AND canonical_input_manifest = {run.CanonicalInputManifest}
                """, cancellationToken);
            if (changed != 1) throw new InvalidOperationException("計算快照在修復時已變更；交易已取消。");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO app.audit_events
                    (id, timestamp, actor_id, organization_id, action, resource_type, resource_id,
                     before_hash, after_hash, correlation_id, metadata_json)
                VALUES ({Guid.NewGuid()}, {DateTimeOffset.UtcNow}, NULL, {run.OrganizationId},
                    'calculation.manifest.original_bytes_restored', 'CalculationRun', {run.Id},
                    {CanonicalManifest.ComputeSha256(run.CanonicalInputManifest)}, {run.InputSha256},
                    {correlationId}, CAST({metadata} AS jsonb))
                """, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return repairs.Count;
    }
}
