using System.Security.Cryptography;
using System.Text;

namespace CarbonFootprint.Domain.Modules.Factors;

public enum GlobalFactorVersionStatus
{
    Draft = 1,
    Published = 2,
    Superseded = 3,
    Withdrawn = 4,
    RemovedFromSource = 5
}

public sealed record GlobalFactor(
    Guid Id,
    string StableSourceKey,
    string SourceOrganization,
    string SourceDataset,
    string OriginalName,
    string NormalizedName,
    DateTimeOffset CreatedAt);

public sealed record GlobalFactorVersion(
    Guid Id,
    Guid GlobalFactorId,
    int VersionNumber,
    decimal Value,
    string NumeratorUnit,
    string DenominatorUnit,
    string Geography,
    string Technology,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    DateOnly? PublicationDate,
    string DatasetVersion,
    string License,
    string SourceUrl,
    string SourceRecordSha256,
    string ImportBatchSha256,
    GlobalFactorVersionStatus Status,
    Guid? SupersedesVersionId,
    DateTimeOffset ImportedAt)
{
    public bool IsSelectable(DateOnly date) =>
        Status == GlobalFactorVersionStatus.Published
        && (ValidFrom is null || ValidFrom <= date)
        && (ValidTo is null || ValidTo >= date);
}

public sealed record GlobalFactorAlias(
    Guid Id,
    Guid GlobalFactorId,
    string Alias,
    string NormalizedAlias,
    string Source);

public sealed record OrganizationFactorActivation(
    Guid OrganizationId,
    Guid GlobalFactorId,
    bool IsEnabled,
    string DisplayAlias,
    string InternalCategory,
    string ApplicabilityNote,
    bool IsProhibited,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

public sealed record OrganizationFactorOverride(
    Guid Id,
    Guid OrganizationId,
    Guid GlobalFactorVersionId,
    decimal? OverrideValue,
    string OverrideGeography,
    string Restriction,
    string Reason,
    DateTimeOffset ApprovedAt,
    string ApprovedBy);

public sealed record OfficialFactorSourceRecord(
    string? StableIdentifier,
    string SourceOrganization,
    string SourceDataset,
    string Name,
    decimal Value,
    string NumeratorUnit,
    string DenominatorUnit,
    string Geography,
    string Technology,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    DateOnly? PublicationDate,
    string DatasetVersion,
    string License,
    string SourceUrl,
    string SourceRecordSha256,
    bool IsWithdrawn);

public sealed record GlobalFactorImportBatch(
    Guid Id,
    string SourceOrganization,
    string SourceDataset,
    string DatasetVersion,
    string BatchSha256,
    DateTimeOffset ImportedAt,
    int SourceRecordCount);

public sealed record GlobalFactorCatalogSnapshot(
    IReadOnlyList<GlobalFactor> Factors,
    IReadOnlyList<GlobalFactorVersion> Versions,
    IReadOnlyList<GlobalFactorAlias> Aliases,
    IReadOnlyList<GlobalFactorImportBatch> Batches);

public sealed record GlobalFactorSyncResult(
    GlobalFactorCatalogSnapshot Catalog,
    GlobalFactorImportBatch Batch,
    IReadOnlyList<Guid> AddedFactorIds,
    IReadOnlyList<Guid> AddedVersionIds,
    IReadOnlyList<Guid> SupersededVersionIds,
    IReadOnlyList<Guid> WithdrawnVersionIds,
    IReadOnlyList<Guid> RemovedVersionIds);

public sealed record FactorProjectReference(
    Guid ProjectVersionId,
    Guid FactorVersionId,
    bool IsActiveProject,
    decimal EmissionsContribution);

public sealed record FactorImpactItem(
    Guid GlobalFactorVersionId,
    GlobalFactorVersionStatus Status,
    IReadOnlyList<Guid> ActiveProjectVersionIds,
    decimal TotalAffectedEmissions);

public static class GlobalFactorCatalogService
{
    public static GlobalFactorSyncResult Synchronize(
        GlobalFactorCatalogSnapshot current,
        IReadOnlyList<OfficialFactorSourceRecord> sourceRecords,
        string batchSha256,
        DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(sourceRecords);
        RequireSha256(batchSha256, nameof(batchSha256));

        var normalizedRecords = sourceRecords
            .Select(record => NormalizeRecord(record))
            .OrderBy(record => BuildStableKey(record), StringComparer.Ordinal)
            .ToArray();

        var sourceOrganizations = normalizedRecords.Select(item => item.SourceOrganization).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sourceDatasets = normalizedRecords.Select(item => item.SourceDataset).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sourceOrganizations.Length != 1 || sourceDatasets.Length != 1)
        {
            throw new InvalidOperationException("One import batch must contain exactly one source organization and dataset.");
        }

        var existingBatch = current.Batches.FirstOrDefault(batch =>
            string.Equals(batch.BatchSha256, batchSha256, StringComparison.OrdinalIgnoreCase));
        if (existingBatch is not null)
        {
            return new(
                current,
                existingBatch,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>());
        }

        var factors = current.Factors.ToList();
        var versions = current.Versions.ToList();
        var aliases = current.Aliases.ToList();
        var batches = current.Batches.ToList();
        var addedFactors = new List<Guid>();
        var addedVersions = new List<Guid>();
        var superseded = new List<Guid>();
        var withdrawn = new List<Guid>();
        var removed = new List<Guid>();
        var seenFactorIds = new HashSet<Guid>();

        foreach (var record in normalizedRecords)
        {
            RequireSha256(record.SourceRecordSha256, nameof(record.SourceRecordSha256));
            var stableKey = BuildStableKey(record);
            var factor = factors.FirstOrDefault(item =>
                string.Equals(item.StableSourceKey, stableKey, StringComparison.Ordinal));
            if (factor is null)
            {
                factor = new(
                    Guid.NewGuid(),
                    stableKey,
                    record.SourceOrganization,
                    record.SourceDataset,
                    record.Name,
                    NormalizeText(record.Name),
                    importedAt);
                factors.Add(factor);
                addedFactors.Add(factor.Id);
            }

            seenFactorIds.Add(factor.Id);
            EnsureAlias(aliases, factor, record.Name);

            var latest = versions
                .Where(version => version.GlobalFactorId == factor.Id)
                .OrderByDescending(version => version.VersionNumber)
                .FirstOrDefault();

            var targetStatus = record.IsWithdrawn
                ? GlobalFactorVersionStatus.Withdrawn
                : GlobalFactorVersionStatus.Published;
            if (latest is not null && IsEquivalent(latest, record, targetStatus))
            {
                continue;
            }

            if (latest is not null && latest.Status == GlobalFactorVersionStatus.Published)
            {
                var supersededVersion = latest with { Status = GlobalFactorVersionStatus.Superseded };
                versions[versions.FindIndex(item => item.Id == latest.Id)] = supersededVersion;
                superseded.Add(latest.Id);
            }

            var newVersion = new GlobalFactorVersion(
                Guid.NewGuid(),
                factor.Id,
                (latest?.VersionNumber ?? 0) + 1,
                record.Value,
                record.NumeratorUnit,
                record.DenominatorUnit,
                record.Geography,
                record.Technology,
                record.ValidFrom,
                record.ValidTo,
                record.PublicationDate,
                record.DatasetVersion,
                record.License,
                record.SourceUrl,
                record.SourceRecordSha256.ToLowerInvariant(),
                batchSha256.ToLowerInvariant(),
                targetStatus,
                latest?.Id,
                importedAt);
            versions.Add(newVersion);
            addedVersions.Add(newVersion.Id);
            if (targetStatus == GlobalFactorVersionStatus.Withdrawn)
            {
                withdrawn.Add(newVersion.Id);
            }
        }

        var scopedFactors = factors.Where(factor =>
            string.Equals(factor.SourceOrganization, sourceOrganizations[0], StringComparison.OrdinalIgnoreCase)
            && string.Equals(factor.SourceDataset, sourceDatasets[0], StringComparison.OrdinalIgnoreCase));
        foreach (var factor in scopedFactors.Where(factor => !seenFactorIds.Contains(factor.Id)))
        {
            var latest = versions
                .Where(version => version.GlobalFactorId == factor.Id)
                .OrderByDescending(version => version.VersionNumber)
                .FirstOrDefault();
            if (latest is null || latest.Status is GlobalFactorVersionStatus.RemovedFromSource or GlobalFactorVersionStatus.Withdrawn)
            {
                continue;
            }

            versions[versions.FindIndex(item => item.Id == latest.Id)] = latest with
            {
                Status = GlobalFactorVersionStatus.RemovedFromSource
            };
            removed.Add(latest.Id);
        }

        var batch = new GlobalFactorImportBatch(
            Guid.NewGuid(),
            sourceOrganizations[0],
            sourceDatasets[0],
            normalizedRecords.Select(item => item.DatasetVersion).Distinct(StringComparer.Ordinal).Single(),
            batchSha256.ToLowerInvariant(),
            importedAt,
            normalizedRecords.Length);
        batches.Add(batch);

        var catalog = new GlobalFactorCatalogSnapshot(
            factors.OrderBy(item => item.StableSourceKey, StringComparer.Ordinal).ToArray(),
            versions.OrderBy(item => item.GlobalFactorId).ThenBy(item => item.VersionNumber).ToArray(),
            aliases.OrderBy(item => item.GlobalFactorId).ThenBy(item => item.NormalizedAlias, StringComparer.Ordinal).ToArray(),
            batches.OrderBy(item => item.ImportedAt).ThenBy(item => item.Id).ToArray());

        return new(
            catalog,
            batch,
            addedFactors,
            addedVersions,
            superseded,
            withdrawn,
            removed);
    }

    public static IReadOnlyList<GlobalFactorVersion> AvailableForOrganization(
        GlobalFactorCatalogSnapshot catalog,
        Guid organizationId,
        DateOnly date,
        IReadOnlyList<OrganizationFactorActivation> activations)
    {
        var activationByFactor = activations
            .Where(item => item.OrganizationId == organizationId)
            .GroupBy(item => item.GlobalFactorId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedAt).First());

        return catalog.Versions
            .Where(version => version.IsSelectable(date))
            .Where(version =>
            {
                if (!activationByFactor.TryGetValue(version.GlobalFactorId, out var activation))
                {
                    return true;
                }

                return activation.IsEnabled && !activation.IsProhibited;
            })
            .OrderBy(version => version.GlobalFactorId)
            .ThenByDescending(version => version.VersionNumber)
            .ToArray();
    }

    public static IReadOnlyList<FactorImpactItem> AnalyzeImpact(
        IReadOnlyList<GlobalFactorVersion> versions,
        IReadOnlyList<FactorProjectReference> references)
    {
        var affectedStatuses = new[]
        {
            GlobalFactorVersionStatus.Superseded,
            GlobalFactorVersionStatus.Withdrawn,
            GlobalFactorVersionStatus.RemovedFromSource
        };

        return versions
            .Where(version => affectedStatuses.Contains(version.Status))
            .OrderBy(version => version.Id)
            .Select(version =>
            {
                var affected = references
                    .Where(reference => reference.FactorVersionId == version.Id && reference.IsActiveProject)
                    .OrderBy(reference => reference.ProjectVersionId)
                    .ToArray();
                return new FactorImpactItem(
                    version.Id,
                    version.Status,
                    affected.Select(reference => reference.ProjectVersionId).Distinct().ToArray(),
                    affected.Sum(reference => reference.EmissionsContribution));
            })
            .Where(item => item.ActiveProjectVersionIds.Count > 0)
            .ToArray();
    }

    public static string BuildStableKey(OfficialFactorSourceRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.StableIdentifier))
        {
            return string.Join(
                ":",
                NormalizeText(record.SourceOrganization),
                NormalizeText(record.SourceDataset),
                NormalizeText(record.StableIdentifier));
        }

        var fallback = string.Join(
            "|",
            NormalizeText(record.SourceOrganization),
            NormalizeText(record.SourceDataset),
            NormalizeText(record.Name),
            NormalizeUnit(record.DenominatorUnit),
            NormalizeText(record.Geography),
            NormalizeText(record.Technology));
        return $"fallback:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fallback))).ToLowerInvariant()}";
    }

    public static string NormalizeText(string value)
    {
        var trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();
        var builder = new StringBuilder(trimmed.Length);
        var previousWhitespace = false;
        foreach (var character in trimmed.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                }
                previousWhitespace = true;
                continue;
            }

            if (char.IsPunctuation(character) && character is not '-' and not '/' and not '.')
            {
                continue;
            }

            previousWhitespace = false;
            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string NormalizeUnit(string value) =>
        NormalizeText(value).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static OfficialFactorSourceRecord NormalizeRecord(OfficialFactorSourceRecord record) => record with
    {
        SourceOrganization = record.SourceOrganization.Trim(),
        SourceDataset = record.SourceDataset.Trim(),
        Name = record.Name.Trim(),
        NumeratorUnit = NormalizeUnit(record.NumeratorUnit),
        DenominatorUnit = NormalizeUnit(record.DenominatorUnit),
        Geography = record.Geography.Trim(),
        Technology = record.Technology.Trim(),
        DatasetVersion = record.DatasetVersion.Trim(),
        License = record.License.Trim(),
        SourceUrl = record.SourceUrl.Trim(),
        SourceRecordSha256 = record.SourceRecordSha256.Trim().ToLowerInvariant()
    };

    private static void EnsureAlias(
        ICollection<GlobalFactorAlias> aliases,
        GlobalFactor factor,
        string alias)
    {
        var normalized = NormalizeText(alias);
        if (aliases.Any(item => item.GlobalFactorId == factor.Id
            && string.Equals(item.NormalizedAlias, normalized, StringComparison.Ordinal)))
        {
            return;
        }

        aliases.Add(new(
            Guid.NewGuid(),
            factor.Id,
            alias.Trim(),
            normalized,
            "official-source"));
    }

    private static bool IsEquivalent(
        GlobalFactorVersion version,
        OfficialFactorSourceRecord record,
        GlobalFactorVersionStatus targetStatus) =>
        version.Value == record.Value
        && string.Equals(version.NumeratorUnit, record.NumeratorUnit, StringComparison.OrdinalIgnoreCase)
        && string.Equals(version.DenominatorUnit, record.DenominatorUnit, StringComparison.OrdinalIgnoreCase)
        && string.Equals(version.Geography, record.Geography, StringComparison.OrdinalIgnoreCase)
        && string.Equals(version.Technology, record.Technology, StringComparison.OrdinalIgnoreCase)
        && version.ValidFrom == record.ValidFrom
        && version.ValidTo == record.ValidTo
        && version.PublicationDate == record.PublicationDate
        && string.Equals(version.DatasetVersion, record.DatasetVersion, StringComparison.Ordinal)
        && string.Equals(version.SourceRecordSha256, record.SourceRecordSha256, StringComparison.OrdinalIgnoreCase)
        && version.Status == targetStatus;

    private static void RequireSha256(string value, string name)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException($"{name} must be a 64-character SHA-256 hexadecimal string.");
        }
    }
}
