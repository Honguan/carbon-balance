using System.Text.Json;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Security.Tests;

public sealed class AuditWriterTests
{
    [Fact]
    public void CanonicalHashIsStableAcrossDecimalScaleUtcOffsetsAndFieldOrder()
    {
        var left = new Dictionary<string, object?> { ["amount"] = 1.2300m, ["at"] = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero) };
        var right = new Dictionary<string, object?> { ["at"] = new DateTimeOffset(2030, 1, 2, 11, 4, 5, TimeSpan.FromHours(8)), ["amount"] = 1.23m };
        Assert.Equal(AuditWriter.StateHash(left), AuditWriter.StateHash(right));
        right["amount"] = 1.2301m;
        Assert.NotEqual(AuditWriter.StateHash(left), AuditWriter.StateHash(right));
    }

    [Fact]
    public void BeforeAfterCaptureUsesExplicitSnapshotAndNeverSaves()
    {
        var organizationId = Guid.NewGuid();
        using var db = CreateDb(organizationId);
        var membership = new OrganizationMembershipRecord { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), Role = "Viewer" };
        db.Attach(membership);
        var original = AuditProjection.Capture(db.Entry(membership), true);
        membership.Role = "Administrator";
        var audit = new AuditWriter(db).AppendTracked("organization.membership.role-changed", db.Entry(membership), membership.UserId, "Authorized change", "request-1");
        Assert.Equal(AuditWriter.StateHash(original), audit.BeforeHash);
        Assert.Equal(AuditWriter.StateHash(AuditProjection.Capture(db.Entry(membership))), audit.AfterHash);
        Assert.NotEqual(audit.BeforeHash, audit.AfterHash);
        Assert.Equal(EntityState.Added, db.Entry(audit).State);
        using var metadata = JsonDocument.Parse(audit.MetadataJson);
        Assert.Equal("Viewer", metadata.RootElement.GetProperty("safeChanges").GetProperty("Role").GetProperty("before").GetString());
        Assert.Equal("Administrator", metadata.RootElement.GetProperty("safeChanges").GetProperty("Role").GetProperty("after").GetString());
    }

    [Fact]
    public void ActorReasonCorrelationAndMetadataDoNotPolluteStateHash()
    {
        var organizationId = Guid.NewGuid();
        using var db = CreateDb(organizationId);
        var state = new Dictionary<string, object?> { ["status"] = "Approved" };
        var writer = new AuditWriter(db);
        var first = writer.Append("inventory.approved", "InventoryProjectVersion", Guid.NewGuid(), organizationId, Guid.NewGuid(), state, state, "First reason", "first");
        var second = writer.Append("inventory.approved", "InventoryProjectVersion", Guid.NewGuid(), organizationId, Guid.NewGuid(), state, state, "Other reason", "second",
            metadata: new Dictionary<string, object?> { ["credentialsChanged"] = true });
        Assert.Equal(first.BeforeHash, second.BeforeHash);
        Assert.Equal(first.AfterHash, second.AfterHash);
        Assert.NotEqual(first.MetadataJson, second.MetadataJson);
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("TokenSha256")]
    [InlineData("EncryptedPassword")]
    [InlineData("PasswordHash")]
    [InlineData("SecurityStamp")]
    [InlineData("Username")]
    public void WriterRejectsForbiddenKeysRatherThanSerializingEntities(string field)
    {
        Assert.Throws<InvalidOperationException>(() => AuditWriter.StateHash(new Dictionary<string, object?> { [field] = "secret-sentinel" }));
        Assert.Throws<InvalidOperationException>(() => AuditWriter.Canonical(new OrganizationRecord { Id = Guid.NewGuid(), Name = "entity" }));
    }

    [Fact]
    public void SmtpProjectionNeverContainsPasswordsEmailOrLoginValues()
    {
        var organizationId = Guid.NewGuid();
        using var db = CreateDb(organizationId);
        var settings = new OrganizationMailSettingsRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Host = "smtp.example.test",
            Port = 587,
            EnableSsl = true,
            Username = "login-secret@example.test",
            EncryptedPassword = "encrypted-secret-sentinel",
            FromAddress = "private-sender@example.test",
            FromName = "private-sender-name"
        };
        db.OrganizationMailSettings.Add(settings);
        var audit = new AuditWriter(db).AppendTracked("organization.mail_settings.updated", db.Entry(settings), Guid.NewGuid(), "Authorized SMTP change", "smtp-request",
            metadata: new Dictionary<string, object?> { ["credentialsChanged"] = true });
        Assert.Null(audit.BeforeHash);
        Assert.NotNull(audit.AfterHash);
        Assert.DoesNotContain("secret-sentinel", audit.MetadataJson);
        Assert.DoesNotContain("private-sender", audit.MetadataJson);
        Assert.DoesNotContain("login-secret", audit.MetadataJson);
        Assert.DoesNotContain("Email", audit.MetadataJson);
        using var metadata = JsonDocument.Parse(audit.MetadataJson);
        Assert.True(metadata.RootElement.GetProperty("credentialsChanged").GetBoolean());
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("export")]
    public void NonMutationsNeedExplicitOperationAndReason(string operation)
    {
        var organizationId = Guid.NewGuid();
        using var db = CreateDb(organizationId);
        var writer = new AuditWriter(db);
        var record = writer.Append("report.exported", "CalculationRun", Guid.NewGuid(), organizationId, null, null, null, "No resource state mutation", "request", operation: operation);
        Assert.Null(record.BeforeHash); Assert.Null(record.AfterHash);
        using var json = JsonDocument.Parse(record.MetadataJson);
        Assert.Equal(operation, json.RootElement.GetProperty("operation").GetString());
        Assert.Equal("No resource state mutation", json.RootElement.GetProperty("noMutationReason").GetString());
        Assert.Throws<InvalidOperationException>(() => writer.Append("bad", "CalculationRun", Guid.NewGuid(), organizationId, null, null, null, "Unspecified mutation", "request"));
    }

    [Fact]
    public void DeletedResourceUsesHashToNullAndRejectsInconsistentOperation()
    {
        var organizationId = Guid.NewGuid();
        using var db = CreateDb(organizationId);
        var membership = new OrganizationMembershipRecord { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), Role = "Viewer" };
        db.OrganizationMemberships.Attach(membership);
        db.OrganizationMemberships.Remove(membership);
        var audit = new AuditWriter(db).AppendTracked("membership.deleted", db.Entry(membership), null, "Explicit deletion fixture", "deletion-request");
        Assert.NotNull(audit.BeforeHash); Assert.Null(audit.AfterHash);
        using var metadata = JsonDocument.Parse(audit.MetadataJson);
        Assert.Equal("delete", metadata.RootElement.GetProperty("operation").GetString());
        Assert.Throws<InvalidOperationException>(() => new AuditWriter(db).Append("bad", "Fixture", Guid.NewGuid(), organizationId,
            null, null, new Dictionary<string, object?> { ["status"] = "Created" }, "Invalid attempt fixture", "request", operation: "attempt"));
    }

    [Fact]
    public void SourceHashIsNamedMetadataAndDoesNotReplaceSafeStateHash()
    {
        var organizationId = Guid.NewGuid();
        using var db = CreateDb(organizationId);
        var evidence = new EvidenceFileRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ActivityDataId = Guid.NewGuid(),
            OriginalFileName = "private-filename",
            ObjectKey = "private-object-key",
            ContentType = "application/pdf",
            SizeBytes = 10,
            Sha256 = new string('a', 64),
            ScanStatus = "Clean"
        };
        db.EvidenceFiles.Add(evidence);
        var writer = new AuditWriter(db);
        var first = writer.AppendTracked("evidence.created", db.Entry(evidence), null, "Evidence fixture", "evidence-request");
        evidence.Sha256 = new string('b', 64);
        var second = writer.AppendTracked("evidence.created", db.Entry(evidence), null, "Evidence fixture", "evidence-request");
        Assert.Equal(first.AfterHash, second.AfterHash);
        Assert.NotEqual(evidence.Sha256, second.AfterHash);
        Assert.DoesNotContain("private-", first.MetadataJson);
        using var metadata = JsonDocument.Parse(first.MetadataJson);
        Assert.Equal(new string('a', 64), metadata.RootElement.GetProperty("evidenceFileSha256").GetString());
    }

    private static CarbonFootprintDbContext CreateDb(Guid organizationId) => new(
        new DbContextOptionsBuilder<CarbonFootprintDbContext>().UseNpgsql("Host=localhost;Database=unused").UseSnakeCaseNamingConvention().Options,
        new Scope(organizationId));

    private sealed record Scope(Guid Id) : IOrganizationScope { public Guid? OrganizationId => Id; }
}
