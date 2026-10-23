using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace CarbonFootprint.Infrastructure.Persistence;

/// <summary>Adds safe, versioned audit evidence to the caller's unit of work; never saves or commits.</summary>
public sealed class AuditWriter(CarbonFootprintDbContext db)
{
    public const string SchemaVersion = "audit-v1";
    public const string HashScope = "safe-resource-projection-v1";

    public AuditEventRecord Append(
        string action, string resourceType, Guid resourceId, Guid organizationId, Guid? actorId,
        IReadOnlyDictionary<string, object?>? before, IReadOnlyDictionary<string, object?>? after,
        string reason, string correlationId, string? sessionReference = null,
        string? operation = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var record = new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actorId,
            OrganizationId = organizationId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            BeforeHash = before is null ? null : StateHash(before),
            AfterHash = after is null ? null : StateHash(after),
            CorrelationId = correlationId,
            MetadataJson = Metadata(before, after, reason, correlationId, sessionReference, operation, metadata)
        };
        db.AuditEvents.Add(record);
        return record;
    }

    public SystemAuditEventRecord AppendSystem(
        string action, string resourceType, Guid resourceId, Guid? actorId, string source,
        IReadOnlyDictionary<string, object?>? before, IReadOnlyDictionary<string, object?>? after,
        string reason, string correlationId, string? sessionReference = null,
        string? operation = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var record = new SystemAuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Source = source,
            BeforeHash = before is null ? null : StateHash(before),
            AfterHash = after is null ? null : StateHash(after),
            CorrelationId = correlationId,
            MetadataJson = Metadata(before, after, reason, correlationId, sessionReference, operation, metadata)
        };
        db.SystemAuditEvents.Add(record);
        return record;
    }

    public AuditEventRecord AppendTracked(string action, EntityEntry entry, Guid? actorId, string reason,
        string correlationId, string? sessionReference = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        db.ChangeTracker.DetectChanges();
        var before = entry.State == EntityState.Added ? null : AuditProjection.Capture(entry, original: true);
        var after = entry.State == EntityState.Deleted ? null : AuditProjection.Capture(entry, original: false);
        var organizationId = entry.Entity is OrganizationRecord organization
            ? organization.Id : (Guid)entry.Property("OrganizationId").CurrentValue!;
        var namedMetadata = new Dictionary<string, object?>(metadata ?? new Dictionary<string, object?>());
        if (entry.Entity is OrganizationMailSettingsRecord)
            namedMetadata["credentialsChanged"] = namedMetadata.GetValueOrDefault("credentialsChanged") is true
                || new[] { "Username", "EncryptedPassword" }.Any(field => entry.State == EntityState.Added
                    ? !string.IsNullOrEmpty(entry.Property(field).CurrentValue as string)
                    : !Equals(entry.Property(field).OriginalValue, entry.Property(field).CurrentValue));
        var sourceField = entry.Entity switch
        {
            PcrVersionRecord or EmissionFactorVersionRecord => (Field: "OriginalDocumentSha256", Name: "sourceDocumentSha256"),
            EvidenceFileRecord => (Field: "Sha256", Name: "evidenceFileSha256"),
            ActivityDataRecord => (Field: "EvidenceSha256", Name: "evidenceFileSha256"),
            _ => default
        };
        if (sourceField.Field is not null && entry.Property(sourceField.Field).CurrentValue is string sourceHash
            && !string.IsNullOrWhiteSpace(sourceHash)) namedMetadata.TryAdd(sourceField.Name, sourceHash);
        return Append(action, AuditProjection.ResourceType(entry.Metadata.ClrType), (Guid)entry.Property("Id").CurrentValue!,
            organizationId, actorId, before, after, reason, correlationId, sessionReference, metadata: namedMetadata);
    }

    public static string StateHash(IReadOnlyDictionary<string, object?> state) => Sha256(Canonical(new Dictionary<string, object?>
    {
        ["schemaVersion"] = SchemaVersion,
        ["hashScope"] = HashScope,
        ["state"] = state
    }));

    public static string SessionReference(string? sessionId) => string.IsNullOrEmpty(sessionId) ? string.Empty : Sha256(sessionId);

    public static string? RequestSessionReference(HttpContext? context)
    {
        var properties = context?.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Properties;
        if (properties is null) return null;
        properties.Items.TryGetValue("session_started_at", out var startedAt);
        return Sha256($"{context!.User.FindFirstValue(ClaimTypes.NameIdentifier)}:{properties.IssuedUtc:O}:{startedAt}");
    }

    public static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Metadata(IReadOnlyDictionary<string, object?>? before, IReadOnlyDictionary<string, object?>? after,
        string reason, string correlationId, string? sessionReference, string? operation, IReadOnlyDictionary<string, object?>? extra)
    {
        operation ??= before is null ? "create" : after is null ? "delete" : "update";
        var mutationOperation = before is null ? "create" : after is null ? "delete" : "update";
        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(correlationId)
            || (before is null && after is null ? operation is not ("attempt" or "export") : operation != mutationOperation))
            throw new InvalidOperationException("Audit 必須提供原因、correlation 與明確的 mutation／非 mutation 語意。");
        var fields = (before?.Keys ?? []).Union(after?.Keys ?? []).Order(StringComparer.Ordinal)
            .Where(field => before is null || after is null
                || Canonical(before.GetValueOrDefault(field)) != Canonical(after.GetValueOrDefault(field))).ToList();
        var changes = fields.ToDictionary(field => field, field => (object?)new Dictionary<string, object?>
        {
            ["before"] = before?.GetValueOrDefault(field),
            ["after"] = after?.GetValueOrDefault(field)
        });
        if (extra?.GetValueOrDefault("credentialsChanged") is true)
        {
            fields.Add("credentialsChanged");
            changes["credentialsChanged"] = true;
        }
        var envelope = new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["operation"] = operation,
            ["hashScope"] = HashScope,
            ["changedFields"] = fields.Order(StringComparer.Ordinal).ToArray(),
            ["safeChanges"] = changes,
            ["reason"] = reason,
            ["sessionReference"] = sessionReference,
            ["correlationId"] = correlationId,
            ["noMutationReason"] = operation is "attempt" or "export" ? reason : null
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, object?>())
            if (!envelope.TryAdd(key, value)) throw new InvalidOperationException("Audit metadata 不可覆寫契約欄位。");
        return Canonical(envelope);
    }

    public static string Canonical(object? value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case IReadOnlyDictionary<string, object?> map:
                writer.WriteStartObject();
                foreach (var (key, item) in map.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (new[] { "password", "token", "email", "securitystamp", "concurrencystamp", "username", "fromaddress", "fromname" }
                        .Any(secret => key.Contains(secret, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Audit projection 不允許憑證或敏感欄位。");
                    writer.WritePropertyName(key); Write(writer, item);
                }
                writer.WriteEndObject(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool boolean: writer.WriteBooleanValue(boolean); break;
            case Guid id: writer.WriteStringValue(id.ToString("D")); break;
            case decimal number: writer.WriteRawValue(number.ToString("0.############################", CultureInfo.InvariantCulture)); break;
            case int number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case DateOnly date: writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset date: writer.WriteStringValue(date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture)); break;
            case Enum enumeration: writer.WriteStringValue(enumeration.ToString()); break;
            case System.Collections.IEnumerable items:
                writer.WriteStartArray(); foreach (var item in items) Write(writer, item); writer.WriteEndArray(); break;
            default: throw new InvalidOperationException("Audit projection 只接受明確安全的 scalar、dictionary 與 array。");
        }
    }
}
