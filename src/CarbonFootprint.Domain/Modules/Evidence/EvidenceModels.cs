using System.Security.Cryptography;

namespace CarbonFootprint.Domain.Modules.Evidence;

public enum EvidenceCategory
{
    Invoice = 1,
    UtilityBill = 2,
    MeterRecord = 3,
    SupplierDeclaration = 4,
    TransportDocument = 5,
    LaboratoryReport = 6,
    CalculationWorkbook = 7,
    Correspondence = 8,
    StandardOrPcr = 9,
    FactorSource = 10,
    Other = 99
}

public enum EvidenceScanStatus
{
    Pending = 1,
    Clean = 2,
    Infected = 3,
    Failed = 4
}

public enum EvidenceStorageStatus
{
    Pending = 1,
    Available = 2,
    Quarantined = 3,
    RetentionLocked = 4,
    DeletedAfterRetention = 5
}

public enum EvidenceLinkTargetType
{
    Activity = 1,
    FactorVersion = 2,
    PcrVersion = 3,
    AllocationPoolVersion = 4,
    Facility = 5,
    ProductVersion = 6,
    CalculationRun = 7,
    ReviewFinding = 8,
    VerificationRecord = 9
}

public enum EvidenceAccessAction
{
    ViewMetadata = 1,
    Download = 2,
    Link = 3,
    Unlink = 4,
    Replace = 5,
    RetentionLock = 6
}

public sealed record EvidenceDocument(
    Guid Id,
    Guid OrganizationId,
    string Title,
    EvidenceCategory Category,
    DateOnly? CoverageStart,
    DateOnly? CoverageEnd,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    bool IsSensitive);

public sealed record EvidenceHash(
    string Algorithm,
    string Value,
    DateTimeOffset ComputedAt,
    string ComputedByService)
{
    public bool IsVerifiedSha256 =>
        string.Equals(Algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase)
        && Value.Length == 64
        && Value.All(Uri.IsHexDigit);
}

public sealed record EvidenceScanResult(
    EvidenceScanStatus Status,
    string Engine,
    string EngineVersion,
    string SignatureVersion,
    DateTimeOffset ScannedAt,
    string Details);

public sealed record EvidenceDocumentVersion(
    Guid Id,
    Guid DocumentId,
    int VersionNumber,
    string OriginalFileName,
    string NormalizedContentType,
    long SizeBytes,
    string ObjectStorageKey,
    string ObjectStorageVersion,
    EvidenceHash Hash,
    EvidenceScanResult Scan,
    EvidenceStorageStatus StorageStatus,
    DateTimeOffset UploadedAt,
    string UploadedBy,
    Guid? ReplacesVersionId = null)
{
    public bool IsUsable =>
        Hash.IsVerifiedSha256
        && Scan.Status == EvidenceScanStatus.Clean
        && StorageStatus is EvidenceStorageStatus.Available or EvidenceStorageStatus.RetentionLocked;
}

public sealed record EvidenceLink(
    Guid Id,
    Guid OrganizationId,
    Guid DocumentVersionId,
    EvidenceLinkTargetType TargetType,
    Guid TargetId,
    string Purpose,
    bool IsRequired,
    DateTimeOffset LinkedAt,
    string LinkedBy);

public sealed record EvidenceAccessLog(
    Guid Id,
    Guid OrganizationId,
    Guid DocumentVersionId,
    EvidenceAccessAction Action,
    string ActorId,
    DateTimeOffset OccurredAt,
    string IpAddressHash,
    string Reason);

public sealed record EvidenceRetentionPolicy(
    Guid Id,
    Guid OrganizationId,
    string Name,
    TimeSpan MinimumRetention,
    bool LockOnSubmission,
    bool LockOnApproval,
    bool LockOnVerification,
    bool LockOnPublication,
    bool AllowLegalHold,
    int VersionNumber,
    bool IsPublished);

public sealed record EvidenceRetentionLock(
    Guid Id,
    Guid DocumentVersionId,
    Guid PolicyId,
    DateTimeOffset LockedAt,
    DateTimeOffset RetainUntil,
    string Trigger,
    string LockedBy,
    bool IsLegalHold);

public sealed record EvidenceRepositorySnapshot(
    IReadOnlyList<EvidenceDocument> Documents,
    IReadOnlyList<EvidenceDocumentVersion> Versions,
    IReadOnlyList<EvidenceLink> Links,
    IReadOnlyList<EvidenceAccessLog> AccessLogs,
    IReadOnlyList<EvidenceRetentionLock> RetentionLocks);

public sealed record EvidenceUploadRequest(
    Guid OrganizationId,
    Guid? ExistingDocumentId,
    string Title,
    EvidenceCategory Category,
    DateOnly? CoverageStart,
    DateOnly? CoverageEnd,
    string OriginalFileName,
    string ContentType,
    byte[] Bytes,
    string UploadedBy,
    DateTimeOffset UploadedAt,
    EvidenceScanResult ScanResult,
    string ObjectStorageVersion,
    bool IsSensitive);

public sealed record EvidenceUploadResult(
    EvidenceRepositorySnapshot Repository,
    EvidenceDocument Document,
    EvidenceDocumentVersion Version,
    bool ReusedPhysicalObject);

public sealed record EvidencePublishGateResult(
    bool CanPublish,
    IReadOnlyList<string> BlockingCodes);

public static class EvidenceDocumentService
{
    public static EvidenceUploadResult Upload(
        EvidenceRepositorySnapshot current,
        EvidenceUploadRequest request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Bytes);

        if (request.Bytes.Length == 0)
        {
            throw new InvalidOperationException("Evidence file cannot be empty.");
        }

        if (request.CoverageStart is not null
            && request.CoverageEnd is not null
            && request.CoverageStart > request.CoverageEnd)
        {
            throw new InvalidOperationException("Evidence coverage start cannot be later than the end date.");
        }

        if (request.ScanResult.Status != EvidenceScanStatus.Clean)
        {
            throw new InvalidOperationException("Evidence cannot become available until the uploaded bytes pass malware scanning.");
        }

        var documents = current.Documents.ToList();
        var versions = current.Versions.ToList();
        var hashValue = Convert.ToHexString(SHA256.HashData(request.Bytes)).ToLowerInvariant();
        var existingPhysical = versions.FirstOrDefault(version =>
            version.Hash.IsVerifiedSha256
            && string.Equals(version.Hash.Value, hashValue, StringComparison.OrdinalIgnoreCase)
            && version.SizeBytes == request.Bytes.LongLength
            && version.StorageStatus is EvidenceStorageStatus.Available or EvidenceStorageStatus.RetentionLocked);

        EvidenceDocument document;
        EvidenceDocumentVersion? previous = null;
        if (request.ExistingDocumentId is null)
        {
            document = new(
                Guid.NewGuid(),
                request.OrganizationId,
                request.Title.Trim(),
                request.Category,
                request.CoverageStart,
                request.CoverageEnd,
                request.UploadedAt,
                request.UploadedBy,
                request.IsSensitive);
            documents.Add(document);
        }
        else
        {
            document = documents.SingleOrDefault(item => item.Id == request.ExistingDocumentId.Value)
                ?? throw new InvalidOperationException("Evidence document does not exist.");
            if (document.OrganizationId != request.OrganizationId)
            {
                throw new InvalidOperationException("Evidence document belongs to another organization.");
            }

            previous = versions
                .Where(item => item.DocumentId == document.Id)
                .OrderByDescending(item => item.VersionNumber)
                .FirstOrDefault();
            if (previous is not null && IsRetentionLocked(current, previous.Id, request.UploadedAt))
            {
                throw new InvalidOperationException("A retention-locked evidence version cannot be replaced. Create a separate logical document instead.");
            }
        }

        var versionNumber = versions
            .Where(item => item.DocumentId == document.Id)
            .Select(item => item.VersionNumber)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var storageKey = existingPhysical?.ObjectStorageKey
            ?? $"sha256/{hashValue[..2]}/{hashValue}";
        var storageVersion = existingPhysical?.ObjectStorageVersion
            ?? request.ObjectStorageVersion;

        var version = new EvidenceDocumentVersion(
            Guid.NewGuid(),
            document.Id,
            versionNumber,
            request.OriginalFileName.Trim(),
            NormalizeContentType(request.ContentType),
            request.Bytes.LongLength,
            storageKey,
            storageVersion,
            new EvidenceHash("SHA-256", hashValue, request.UploadedAt, "server-upload-pipeline"),
            request.ScanResult,
            EvidenceStorageStatus.Available,
            request.UploadedAt,
            request.UploadedBy,
            previous?.Id);
        versions.Add(version);

        var repository = current with
        {
            Documents = documents.OrderBy(item => item.Id).ToArray(),
            Versions = versions.OrderBy(item => item.DocumentId).ThenBy(item => item.VersionNumber).ToArray()
        };

        return new(repository, document, version, existingPhysical is not null);
    }

    public static EvidenceRepositorySnapshot Link(
        EvidenceRepositorySnapshot current,
        EvidenceLink link)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(link);
        var version = current.Versions.SingleOrDefault(item => item.Id == link.DocumentVersionId)
            ?? throw new InvalidOperationException("Evidence version does not exist.");
        var document = current.Documents.Single(item => item.Id == version.DocumentId);
        if (document.OrganizationId != link.OrganizationId)
        {
            throw new InvalidOperationException("Evidence cannot be linked across organizations.");
        }

        if (!version.IsUsable)
        {
            throw new InvalidOperationException("Only clean, hash-verified and available evidence versions can be linked.");
        }

        if (current.Links.Any(item =>
            item.DocumentVersionId == link.DocumentVersionId
            && item.TargetType == link.TargetType
            && item.TargetId == link.TargetId
            && string.Equals(item.Purpose, link.Purpose, StringComparison.Ordinal)))
        {
            return current;
        }

        return current with
        {
            Links = current.Links.Append(link)
                .OrderBy(item => item.TargetType)
                .ThenBy(item => item.TargetId)
                .ThenBy(item => item.Id)
                .ToArray()
        };
    }

    public static EvidenceRepositorySnapshot RecordAccess(
        EvidenceRepositorySnapshot current,
        EvidenceAccessLog access)
    {
        if (!current.Versions.Any(item => item.Id == access.DocumentVersionId))
        {
            throw new InvalidOperationException("Evidence version does not exist.");
        }

        return current with
        {
            AccessLogs = current.AccessLogs.Append(access)
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.Id)
                .ToArray()
        };
    }

    public static EvidenceRepositorySnapshot ApplyRetentionLock(
        EvidenceRepositorySnapshot current,
        EvidenceRetentionPolicy policy,
        Guid documentVersionId,
        string trigger,
        string actorId,
        DateTimeOffset lockedAt,
        bool legalHold = false)
    {
        if (!policy.IsPublished)
        {
            throw new InvalidOperationException("Only published retention policies can be applied.");
        }

        var version = current.Versions.SingleOrDefault(item => item.Id == documentVersionId)
            ?? throw new InvalidOperationException("Evidence version does not exist.");
        var document = current.Documents.Single(item => item.Id == version.DocumentId);
        if (document.OrganizationId != policy.OrganizationId)
        {
            throw new InvalidOperationException("Retention policy and evidence document belong to different organizations.");
        }

        if (legalHold && !policy.AllowLegalHold)
        {
            throw new InvalidOperationException("This retention policy does not allow legal hold.");
        }

        var retainUntil = legalHold
            ? DateTimeOffset.MaxValue
            : lockedAt.Add(policy.MinimumRetention);
        var retentionLock = new EvidenceRetentionLock(
            Guid.NewGuid(),
            documentVersionId,
            policy.Id,
            lockedAt,
            retainUntil,
            trigger,
            actorId,
            legalHold);
        var versions = current.Versions
            .Select(item => item.Id == documentVersionId
                ? item with { StorageStatus = EvidenceStorageStatus.RetentionLocked }
                : item)
            .ToArray();

        return current with
        {
            Versions = versions,
            RetentionLocks = current.RetentionLocks.Append(retentionLock)
                .OrderBy(item => item.LockedAt)
                .ThenBy(item => item.Id)
                .ToArray()
        };
    }

    public static EvidencePublishGateResult ValidatePublishGate(
        EvidenceRepositorySnapshot repository,
        EvidenceLinkTargetType targetType,
        Guid targetId,
        IReadOnlySet<EvidenceCategory> requiredCategories)
    {
        var linkedVersions = repository.Links
            .Where(link => link.TargetType == targetType && link.TargetId == targetId)
            .Join(
                repository.Versions,
                link => link.DocumentVersionId,
                version => version.Id,
                (_, version) => version)
            .ToArray();
        var linkedDocuments = linkedVersions
            .Join(
                repository.Documents,
                version => version.DocumentId,
                document => document.Id,
                (version, document) => new { Version = version, Document = document })
            .ToArray();
        var codes = new List<string>();

        foreach (var category in requiredCategories.OrderBy(value => value))
        {
            if (!linkedDocuments.Any(item => item.Document.Category == category && item.Version.IsUsable))
            {
                codes.Add($"EVIDENCE-CATEGORY-{category.ToString().ToUpperInvariant()}");
            }
        }

        if (linkedVersions.Any(version => !version.Hash.IsVerifiedSha256))
        {
            codes.Add("EVIDENCE-HASH");
        }

        if (linkedVersions.Any(version => version.Scan.Status != EvidenceScanStatus.Clean))
        {
            codes.Add("EVIDENCE-SCAN");
        }

        if (linkedVersions.Any(version => version.StorageStatus is EvidenceStorageStatus.Quarantined or EvidenceStorageStatus.DeletedAfterRetention))
        {
            codes.Add("EVIDENCE-STORAGE");
        }

        var ordered = codes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray();
        return new(ordered.Length == 0, ordered);
    }

    public static bool IsRetentionLocked(
        EvidenceRepositorySnapshot repository,
        Guid versionId,
        DateTimeOffset at) =>
        repository.RetentionLocks.Any(item =>
            item.DocumentVersionId == versionId
            && (item.IsLegalHold || item.RetainUntil > at));

    private static string NormalizeContentType(string value)
    {
        var contentType = (value ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType;
    }
}
