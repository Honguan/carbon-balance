using System.Data;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CarbonFootprint.Domain.Modules.Calculations;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Persistence;

public sealed record AuditArchiveArtifact(byte[] Bytes, string AuditJsonSha256, DateTimeOffset? CutoffTimestamp, Guid? CutoffId);

/// <summary>Bounded verification artifact for an existing run; does not implement governance approval or certification.</summary>
public sealed class AuditArchive(CarbonFootprintDbContext db, IOrganizationScope organizationScope)
{
    public async Task<AuditArchiveArtifact?> BuildAsync(Guid runId, CancellationToken cancellationToken,
        DateTimeOffset? cutoffTimestamp = null, Guid? cutoffId = null)
    {
        if (!organizationScope.OrganizationId.HasValue) return null;
        if (cutoffTimestamp.HasValue != cutoffId.HasValue) throw new ArgumentException("Audit cutoff 必須同時提供 timestamp 與 ID。");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var organizationId = organizationScope.OrganizationId.Value;
        var run = await db.CalculationRuns.AsNoTracking().SingleOrDefaultAsync(
            item => item.OrganizationId == organizationId && item.Id == runId, cancellationToken);
        if (run is null) return null;
        if (!CanonicalManifest.HasValidSha256(run.CanonicalInputManifest, run.InputSha256))
            throw new InvalidOperationException("Calculation manifest 雜湊不符，無法匯出稽核 archive。");
        var lines = await db.CalculationLineItems.AsNoTracking().Where(item => item.CalculationRunId == runId).ToArrayAsync(cancellationToken);
        // Only immutable run references seed discovery; current project/PCR/evidence links can change after a cutoff.
        var resources = new HashSet<Guid> { runId, run.ProjectVersionId };
        using var manifest = JsonDocument.Parse(run.CanonicalInputManifest);
        if (manifest.RootElement.TryGetProperty("productVersionId", out var productVersion)
            && productVersion.TryGetGuid(out var productVersionId)) resources.Add(productVersionId);
        foreach (var line in lines) { resources.Add(line.ActivityId); resources.Add(line.FactorVersionId); }
        var directEvents = await db.AuditEvents.AsNoTracking().Where(item => item.OrganizationId == organizationId && resources.Contains(item.ResourceId))
            .ToArrayAsync(cancellationToken);
        bool Included(AuditEventRecord item) => !cutoffTimestamp.HasValue || item.Timestamp < cutoffTimestamp
            || (item.Timestamp == cutoffTimestamp && string.CompareOrdinal(item.Id.ToString("D"), cutoffId!.Value.ToString("D")) <= 0);
        var correlations = directEvents.Where(Included).Select(item => item.CorrelationId).Distinct().ToArray();
        var events = await db.AuditEvents.AsNoTracking().Where(item => item.OrganizationId == organizationId
                && (resources.Contains(item.ResourceId) || correlations.Contains(item.CorrelationId)))
            .OrderBy(item => item.Timestamp).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
        if (cutoffTimestamp.HasValue)
            events = events.Where(Included).ToArray();
        else if (events.Length > 0) { cutoffTimestamp = events[^1].Timestamp; cutoffId = events[^1].Id; }
        var rows = events.Select(item => new Dictionary<string, object?>
        {
            ["id"] = item.Id,
            ["timestamp"] = item.Timestamp,
            ["actorId"] = item.ActorId,
            ["organizationId"] = item.OrganizationId,
            ["action"] = item.Action,
            ["resourceType"] = item.ResourceType,
            ["resourceId"] = item.ResourceId,
            ["beforeHash"] = item.BeforeHash,
            ["afterHash"] = item.AfterHash,
            ["correlationId"] = item.CorrelationId,
            ["metadata"] = ReadMetadata(item.MetadataJson)
        }).ToArray();
        var auditJson = AuditWriter.Canonical(new Dictionary<string, object?>
        {
            ["archiveSchemaVersion"] = "audit-archive-v1",
            ["organizationId"] = organizationId,
            ["runId"] = runId,
            ["projectVersionId"] = run.ProjectVersionId,
            ["inputManifestSha256"] = run.InputSha256,
            ["cutoffTimestamp"] = cutoffTimestamp,
            ["cutoffId"] = cutoffId,
            ["resourceIds"] = events.Select(item => item.ResourceId).Append(runId).Append(run.ProjectVersionId)
                .Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray(),
            ["events"] = rows,
            ["scope"] = "tenant immutable run/project/activity/factor references and cutoff-bounded shared correlations; excludes global system audit",
            ["pcrReference"] = "unknown: existing canonical manifest contains a PCR label but no immutable PCR resource ID; no current project inference"
        });
        var auditHash = AuditWriter.Sha256(auditJson);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "audit.json", auditJson);
            WriteEntry(zip, "audit.sha256", auditHash + "  audit.json\n");
            WriteEntry(zip, "canonical-input-manifest.json", run.CanonicalInputManifest);
            WriteEntry(zip, "canonical-input-manifest.sha256", run.InputSha256 + "  canonical-input-manifest.json\n");
        }
        await transaction.CommitAsync(cancellationToken);
        return new AuditArchiveArtifact(buffer.ToArray(), auditHash, cutoffTimestamp, cutoffId);
    }

    private static object? ReadMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
        static object? Read(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => Read(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(Read).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new InvalidOperationException("Audit metadata 格式無效。")
        };
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
