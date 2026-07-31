using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CarbonFootprint.Domain.Modules.Verification;

public sealed record VerificationArchiveFile(
    string Path,
    byte[] Content,
    string MediaType);

public sealed record VerificationArchiveIndexItem(
    string Path,
    long SizeBytes,
    string Sha256,
    string MediaType);

public sealed record VerificationArchiveResult(
    byte[] ArchiveBytes,
    string ArchiveSha256,
    IReadOnlyList<VerificationArchiveIndexItem> Files,
    string ExportSchemaVersion,
    DateTimeOffset GeneratedAt);

public sealed record VerificationArchiveMetadata(
    Guid ProjectVersionId,
    Guid CalculationRunId,
    string EngineBuild,
    string RuleSetVersion,
    string PcrVersion,
    string GwpVersion,
    string UnitCatalogueVersion,
    IReadOnlyList<string> FormulaVersions,
    IReadOnlyList<Guid> FactorVersionIds,
    string ExportSchemaVersion,
    string CalculationManifestSha256,
    DateTimeOffset GeneratedAt);

public enum ProjectChangeType
{
    Added = 1,
    Removed = 2,
    Changed = 3,
    Unchanged = 4
}

public sealed record ProjectEntitySnapshot(
    string EntityType,
    string EntityKey,
    string CanonicalSha256,
    decimal Emissions,
    string LifecycleStage);

public sealed record ProjectChangeItem(
    string EntityType,
    string EntityKey,
    ProjectChangeType ChangeType,
    string PreviousSha256,
    string CurrentSha256,
    decimal PreviousEmissions,
    decimal CurrentEmissions,
    decimal AbsoluteDelta,
    decimal? PercentageDelta,
    string LifecycleStage);

public sealed record ProjectVersionComparison(
    IReadOnlyList<ProjectChangeItem> Changes,
    decimal PreviousTotal,
    decimal CurrentTotal,
    decimal AbsoluteDelta,
    decimal? PercentageDelta,
    IReadOnlyList<ProjectChangeItem> Hotspots);

public enum GovernedChangeType
{
    FactorSuperseded = 1,
    FactorWithdrawn = 2,
    PcrExpired = 3,
    PcrWithdrawn = 4,
    FormulaUpdated = 5,
    FormulaWithdrawn = 6,
    UnitCatalogueChanged = 7,
    EvidenceInvalidated = 8
}

public sealed record GovernedDependencyChange(
    GovernedChangeType ChangeType,
    string DependencyType,
    string DependencyKey,
    string PreviousVersion,
    string CurrentVersion,
    DateTimeOffset EffectiveAt,
    string Reason);

public sealed record ProjectDependencyReference(
    Guid ProjectVersionId,
    bool IsActive,
    string DependencyType,
    string DependencyKey,
    decimal AffectedEmissions,
    string LifecycleStage);

public sealed record ProjectImpactResult(
    GovernedDependencyChange Change,
    IReadOnlyList<Guid> AffectedProjectVersionIds,
    decimal TotalAffectedEmissions,
    IReadOnlyDictionary<string, decimal> EmissionsByStage);

public static class VerificationArchiveBuilder
{
    private static readonly DateTimeOffset DeterministicZipTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static VerificationArchiveResult Build(
        VerificationArchiveMetadata metadata,
        IReadOnlyList<VerificationArchiveFile> files)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(files);
        RequireSha256(metadata.CalculationManifestSha256, nameof(metadata.CalculationManifestSha256));

        var requiredPaths = new[]
        {
            "report/inventory-report.html",
            "workbook/inventory.xlsx",
            "manifest/canonical-manifest.json",
            "calculation/line-items.csv",
            "calculation/stage-summary.csv",
            "register/factors.csv",
            "trace/unit-conversions.csv",
            "trace/allocations.csv",
            "evidence/index.csv",
            "validation/readiness.json",
            "review/findings.json",
            "verification/records.json",
            "audit/events.json"
        };
        var normalizedFiles = files
            .Select(NormalizeFile)
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();

        if (normalizedFiles.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != normalizedFiles.Length)
        {
            throw new InvalidOperationException("Verification archive contains duplicate file paths.");
        }

        var missing = requiredPaths
            .Where(path => normalizedFiles.All(file => !string.Equals(file.Path, path, StringComparison.Ordinal)))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Verification archive is missing required files: {string.Join(", ", missing)}.");
        }

        var metadataFile = new VerificationArchiveFile(
            "metadata/export.txt",
            Encoding.UTF8.GetBytes(CreateMetadataText(metadata)),
            "text/plain; charset=utf-8");
        var allPayloadFiles = normalizedFiles.Append(metadataFile)
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        var index = allPayloadFiles.Select(file => new VerificationArchiveIndexItem(
            file.Path,
            file.Content.LongLength,
            Sha256(file.Content),
            file.MediaType)).ToArray();
        var hashes = string.Join(
            "\n",
            index.Select(item => $"{item.Sha256}  {item.Path}")) + "\n";
        var hashesFile = new VerificationArchiveFile(
            "hashes.sha256",
            Encoding.UTF8.GetBytes(hashes),
            "text/plain; charset=utf-8");

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            foreach (var file in allPayloadFiles.Append(hashesFile).OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.NoCompression);
                entry.LastWriteTime = DeterministicZipTimestamp;
                using var entryStream = entry.Open();
                entryStream.Write(file.Content, 0, file.Content.Length);
            }
        }

        var bytes = stream.ToArray();
        return new(
            bytes,
            Sha256(bytes),
            index.Append(new(
                hashesFile.Path,
                hashesFile.Content.LongLength,
                Sha256(hashesFile.Content),
                hashesFile.MediaType)).ToArray(),
            metadata.ExportSchemaVersion,
            metadata.GeneratedAt);
    }

    public static bool Verify(VerificationArchiveResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(Sha256(result.ArchiveBytes), result.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using var stream = new MemoryStream(result.ArchiveBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        foreach (var item in result.Files)
        {
            if (!entries.TryGetValue(item.Path, out var entry))
            {
                return false;
            }

            using var entryStream = entry.Open();
            using var content = new MemoryStream();
            entryStream.CopyTo(content);
            if (content.Length != item.SizeBytes
                || !string.Equals(Sha256(content.ToArray()), item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static VerificationArchiveFile NormalizeFile(VerificationArchiveFile file)
    {
        if (file.Content is null)
        {
            throw new InvalidOperationException("Verification archive file content is required.");
        }

        var path = (file.Path ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains("../", StringComparison.Ordinal)
            || path.StartsWith("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Verification archive file path is invalid.");
        }

        if (string.Equals(path, "hashes.sha256", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("hashes.sha256 is generated by the archive builder.");
        }

        return file with
        {
            Path = path,
            MediaType = string.IsNullOrWhiteSpace(file.MediaType)
                ? "application/octet-stream"
                : file.MediaType.Trim()
        };
    }

    private static string CreateMetadataText(VerificationArchiveMetadata metadata) =>
        string.Join(
            "\n",
            $"projectVersionId={metadata.ProjectVersionId:D}",
            $"calculationRunId={metadata.CalculationRunId:D}",
            $"engineBuild={metadata.EngineBuild}",
            $"ruleSetVersion={metadata.RuleSetVersion}",
            $"pcrVersion={metadata.PcrVersion}",
            $"gwpVersion={metadata.GwpVersion}",
            $"unitCatalogueVersion={metadata.UnitCatalogueVersion}",
            $"formulaVersions={string.Join(',', metadata.FormulaVersions.OrderBy(value => value, StringComparer.Ordinal))}",
            $"factorVersionIds={string.Join(',', metadata.FactorVersionIds.OrderBy(value => value).Select(value => value.ToString("D")))}",
            $"exportSchemaVersion={metadata.ExportSchemaVersion}",
            $"calculationManifestSha256={metadata.CalculationManifestSha256}") + "\n";

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RequireSha256(string value, string name)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException($"{name} must be a 64-character SHA-256 hexadecimal string.");
        }
    }
}

public static class ProjectVersionComparisonService
{
    public static ProjectVersionComparison Compare(
        IReadOnlyList<ProjectEntitySnapshot> previous,
        IReadOnlyList<ProjectEntitySnapshot> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var previousByKey = previous.ToDictionary(Key, StringComparer.Ordinal);
        var currentByKey = current.ToDictionary(Key, StringComparer.Ordinal);
        var keys = previousByKey.Keys
            .Union(currentByKey.Keys, StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var changes = new List<ProjectChangeItem>();

        foreach (var key in keys)
        {
            previousByKey.TryGetValue(key, out var before);
            currentByKey.TryGetValue(key, out var after);
            var changeType = before is null
                ? ProjectChangeType.Added
                : after is null
                    ? ProjectChangeType.Removed
                    : string.Equals(before.CanonicalSha256, after.CanonicalSha256, StringComparison.OrdinalIgnoreCase)
                        ? ProjectChangeType.Unchanged
                        : ProjectChangeType.Changed;
            var previousEmissions = before?.Emissions ?? 0m;
            var currentEmissions = after?.Emissions ?? 0m;
            var delta = currentEmissions - previousEmissions;
            var percentage = previousEmissions == 0m
                ? null
                : delta / previousEmissions;

            changes.Add(new(
                after?.EntityType ?? before!.EntityType,
                after?.EntityKey ?? before!.EntityKey,
                changeType,
                before?.CanonicalSha256 ?? string.Empty,
                after?.CanonicalSha256 ?? string.Empty,
                previousEmissions,
                currentEmissions,
                delta,
                percentage,
                after?.LifecycleStage ?? before!.LifecycleStage));
        }

        var previousTotal = previous.Sum(item => item.Emissions);
        var currentTotal = current.Sum(item => item.Emissions);
        var totalDelta = currentTotal - previousTotal;
        var totalPercentage = previousTotal == 0m ? null : totalDelta / previousTotal;
        var hotspots = changes
            .Where(change => change.ChangeType != ProjectChangeType.Unchanged)
            .OrderByDescending(change => Math.Abs(change.AbsoluteDelta))
            .ThenBy(change => change.EntityType, StringComparer.Ordinal)
            .ThenBy(change => change.EntityKey, StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        return new(changes, previousTotal, currentTotal, totalDelta, totalPercentage, hotspots);
    }

    public static IReadOnlyList<ProjectImpactResult> AnalyzeImpact(
        IReadOnlyList<GovernedDependencyChange> changes,
        IReadOnlyList<ProjectDependencyReference> references)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(references);

        return changes
            .OrderBy(change => change.EffectiveAt)
            .ThenBy(change => change.DependencyType, StringComparer.Ordinal)
            .ThenBy(change => change.DependencyKey, StringComparer.Ordinal)
            .Select(change =>
            {
                var affected = references
                    .Where(reference => reference.IsActive
                        && string.Equals(reference.DependencyType, change.DependencyType, StringComparison.Ordinal)
                        && string.Equals(reference.DependencyKey, change.DependencyKey, StringComparison.Ordinal))
                    .OrderBy(reference => reference.ProjectVersionId)
                    .ToArray();
                return new ProjectImpactResult(
                    change,
                    affected.Select(reference => reference.ProjectVersionId).Distinct().ToArray(),
                    affected.Sum(reference => reference.AffectedEmissions),
                    affected.GroupBy(reference => reference.LifecycleStage)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Sum(reference => reference.AffectedEmissions),
                            StringComparer.Ordinal));
            })
            .Where(result => result.AffectedProjectVersionIds.Count > 0)
            .ToArray();
    }

    private static string Key(ProjectEntitySnapshot snapshot) =>
        $"{snapshot.EntityType}:{snapshot.EntityKey}";
}
