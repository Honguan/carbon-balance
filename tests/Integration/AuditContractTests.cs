using System.IO.Compression;
using System.Text.Json;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CarbonFootprint.Integration.Tests;

public sealed class AuditContractTests
{
    [Theory]
    [InlineData("app.audit_events", "UPDATE")]
    [InlineData("app.audit_events", "DELETE")]
    [InlineData("app.audit_events", "TRUNCATE")]
    [InlineData("identity.system_audit_events", "UPDATE")]
    [InlineData("identity.system_audit_events", "DELETE")]
    [InlineData("identity.system_audit_events", "TRUNCATE")]
    public async Task DirectSqlCannotRewriteOrTruncateAuditTables(string table, string operation)
    {
        var id = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        await using (var db = CreateDb(organizationId))
        {
            var writer = new AuditWriter(db);
            var state = new Dictionary<string, object?> { ["id"] = id };
            if (table == "app.audit_events") writer.Append("fixture.created", "Fixture", id, organizationId, null, null, state, "Guard test fixture", id.ToString("N"));
            else writer.AppendSystem("fixture.created", "Fixture", id, null, "isolated-test", null, state, "Guard test fixture", id.ToString("N"));
            await db.SaveChangesAsync();
        }
        await using var admin = new NpgsqlConnection(AdminConnection());
        await admin.OpenAsync();
        var sql = operation switch
        {
            "UPDATE" => $"UPDATE {table} SET action = 'tampered' WHERE resource_id = @id",
            "DELETE" => $"DELETE FROM {table} WHERE resource_id = @id",
            _ => $"TRUNCATE {table}"
        };
        await using var command = new NpgsqlCommand(sql, admin);
        if (operation != "TRUNCATE") command.Parameters.AddWithValue("id", id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("55000", error.SqlState);
    }

    [Fact]
    public async Task NonOwnerRuntimeRoleCanSelectAndInsertButCannotRewriteAudit()
    {
        var roleName = "audit_rt_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(AdminConnection()) { Username = roleName, Password = "isolated-audit-runtime-fixture" };
        await using var admin = new NpgsqlConnection(AdminConnection());
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"""
            CREATE ROLE "{roleName}" LOGIN PASSWORD 'isolated-audit-runtime-fixture';
            GRANT USAGE ON SCHEMA app, identity TO "{roleName}";
            GRANT SELECT, INSERT ON app.audit_events, identity.system_audit_events TO "{roleName}";
            """, admin)) await create.ExecuteNonQueryAsync();
        try
        {
            await using var runtime = new NpgsqlConnection(builder.ConnectionString);
            await runtime.OpenAsync();
            foreach (var table in new[] { "app.audit_events", "identity.system_audit_events" })
            {
                await using var permissions = new NpgsqlCommand($"""
                    SELECT has_table_privilege(current_user, '{table}', 'SELECT')
                        AND has_table_privilege(current_user, '{table}', 'INSERT')
                        AND NOT has_table_privilege(current_user, '{table}', 'UPDATE')
                        AND NOT has_table_privilege(current_user, '{table}', 'DELETE')
                        AND NOT has_table_privilege(current_user, '{table}', 'TRUNCATE')
                        AND (SELECT relowner <> current_user::regrole FROM pg_class WHERE oid = '{table}'::regclass);
                    """, runtime);
                Assert.True((bool)(await permissions.ExecuteScalarAsync())!);
                await using var select = new NpgsqlCommand($"SELECT COUNT(*) FROM {table}", runtime);
                Assert.True((long)(await select.ExecuteScalarAsync())! >= 0);
                foreach (var sql in new[] { $"UPDATE {table} SET action = action", $"DELETE FROM {table}", $"TRUNCATE {table}" })
                {
                    await using var forbidden = new NpgsqlCommand(sql, runtime);
                    var error = await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync());
                    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
                }
            }
            var organizationId = Guid.NewGuid();
            await using var db = CreateDb(organizationId, builder.ConnectionString);
            var audit = new AuditWriter(db).Append("runtime.fixture-created", "Fixture", Guid.NewGuid(), organizationId, null, null,
                new Dictionary<string, object?> { ["status"] = "Created" }, "Isolated runtime role insertion", roleName);
            await db.SaveChangesAsync();
            Assert.NotNull(await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == audit.Id));
        }
        finally
        {
            using var pool = new NpgsqlConnection(builder.ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var drop = new NpgsqlCommand($"DROP OWNED BY \"{roleName}\"; DROP ROLE \"{roleName}\";", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task AuditWriteFailureRollsBackAnAlreadyWrittenMutationInTheSameTransaction()
    {
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var auditId = Guid.Empty;
        await using (var db = CreateDb(organizationId))
        {
            db.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Atomic fixture" });
            db.Products.Add(new ProductRecord { Id = productId, OrganizationId = organizationId, Name = "Before" });
            await db.SaveChangesAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var product = await db.Products.SingleAsync(item => item.Id == productId);
            var before = AuditProjection.Capture(db.Entry(product));
            product.Name = "After";
            await db.SaveChangesAsync();
            var audit = new AuditWriter(db).Append("product.updated", "Product", productId, organizationId, null,
                before, AuditProjection.Capture(db.Entry(product)), "Atomic fixture mutation", new string('x', 101));
            auditId = audit.Id;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
        await using var verify = CreateDb(organizationId);
        Assert.Equal("Before", (await verify.Products.SingleAsync(item => item.Id == productId)).Name);
        Assert.False(await verify.AuditEvents.AnyAsync(item => item.Id == auditId));
    }

    [Fact]
    public async Task DecimalScaleFromFreshDbContextPreservesStateHashAndUpdateContinuity()
    {
        var organizationId = Guid.NewGuid();
        var factorId = Guid.NewGuid();
        string initialHash;
        await using (var db = CreateDb(organizationId))
        {
            db.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Decimal fixture" });
            var factor = new EmissionFactorVersionRecord
            {
                Id = factorId,
                OrganizationId = organizationId,
                FactorId = Guid.NewGuid(),
                VersionNumber = 1,
                Name = "Decimal fixture",
                Value = 1.23m,
                NumeratorUnitCode = "kgCO2e",
                DenominatorUnitCode = "kg",
                Geography = "TW",
                PublicationStatus = "Draft",
                SourceDatasetVersion = "fixture-v1",
                LicenseCode = "fixture"
            };
            db.EmissionFactorVersions.Add(factor);
            var audit = new AuditWriter(db).AppendTracked("factor.version.created", db.Entry(factor), null, "Decimal fixture creation", factorId.ToString("N"));
            initialHash = audit.AfterHash!;
            await db.SaveChangesAsync();
        }
        await using (var fresh = CreateDb(organizationId))
        {
            var factor = await fresh.EmissionFactorVersions.SingleAsync(item => item.Id == factorId);
            Assert.Equal(initialHash, AuditWriter.StateHash(AuditProjection.Capture(fresh.Entry(factor))));
            factor.Value = 2.5m;
            var update = new AuditWriter(fresh).AppendTracked("factor.version.updated", fresh.Entry(factor), null, "Controlled fixture correction", Guid.NewGuid().ToString("N"));
            Assert.Equal(initialHash, update.BeforeHash);
            Assert.NotEqual(update.BeforeHash, update.AfterHash);
            await fresh.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ArchiveIsDeterministicTenantLimitedAndRespectsTimestampIdCutoff()
    {
        var organizationId = Guid.NewGuid();
        var otherOrganization = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var productVersionId = Guid.NewGuid();
        var manifest = JsonSerializer.Serialize(new { productVersionId, fixture = true });
        await using (var db = CreateDb(organizationId))
        {
            var product = new ProductRecord { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Archive fixture" };
            db.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Archive fixture" });
            db.Products.Add(product);
            db.ProductVersions.Add(new ProductVersionRecord { Id = productVersionId, OrganizationId = organizationId, ProductId = product.Id, VersionNumber = 1, NameZhTw = "Archive fixture" });
            db.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
            {
                Id = projectId,
                OrganizationId = organizationId,
                ProductVersionId = productVersionId,
                VersionNumber = 1,
                FunctionalUnit = "fixture",
                PcrVersion = "fixture-v1",
                WorkflowStatus = "Draft"
            });
            var run = new CalculationRunRecord
            {
                Id = runId,
                OrganizationId = organizationId,
                ProjectVersionId = projectId,
                CanonicalInputManifest = manifest,
                InputSha256 = AuditWriter.Sha256(manifest),
                EngineBuild = "fixture",
                RuleSetVersion = "fixture",
                UnitCatalogueVersion = "fixture",
                GwpVersion = "fixture",
                PcrVersion = "fixture-v1",
                DataQualitySummaryJson = "{}"
            };
            db.CalculationRuns.Add(run);
            var unrelated = new AuditWriter(db).Append("older-unrelated-event", "Fixture", Guid.NewGuid(), organizationId, null, null,
                new Dictionary<string, object?> { ["status"] = "Unrelated" }, "Cutoff discovery counterexample", "future-correlation");
            unrelated.Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
            new AuditWriter(db).AppendTracked("calculation.run.created", db.Entry(run), null, "Archive fixture run", "shared-correlation");
            await db.SaveChangesAsync();
        }
        await using (var other = CreateDb(otherOrganization))
        {
            new AuditWriter(other).Append("other-tenant-secret-event", "CalculationRun", runId, otherOrganization, null, null,
                new Dictionary<string, object?> { ["status"] = "Other tenant" }, "Tenant isolation fixture", "shared-correlation");
            await other.SaveChangesAsync();
        }
        AuditArchiveArtifact first;
        await using (var db = CreateDb(organizationId)) first = (await new AuditArchive(db, new Scope(organizationId)).BuildAsync(runId, default))!;
        Assert.NotNull(first.CutoffId);
        await using (var db = CreateDb(organizationId))
        {
            new AuditWriter(db).Append("later-export", "CalculationRun", runId, organizationId, null, null, null,
                "Later export; no resource mutation", "later-correlation", operation: "export");
            new AuditWriter(db).Append("later-related-event", "CalculationRun", runId, organizationId, null, null, null,
                "Future correlation must not discover older unrelated events", "future-correlation", operation: "export");
            var project = await db.InventoryProjectVersions.SingleAsync(item => item.Id == projectId);
            var sameLabelPcr = new PcrVersionRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                RuleSetId = Guid.NewGuid(),
                RegistrationNumber = "fixture",
                VersionNumber = 1,
                Title = "Different stable ID with same label",
                PublicationStatus = "Draft",
                SourceReference = "fixture"
            };
            db.PcrVersions.Add(sameLabelPcr);
            project.PcrVersionId = sameLabelPcr.Id;
            project.PcrVersion = "fixture";
            var pcrAudit = new AuditWriter(db).AppendTracked("same-label-current-pcr", db.Entry(sameLabelPcr), null,
                "Current mutable link must not enter historical archive", "unrelated-pcr-correlation");
            pcrAudit.Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
            var replay = (await new AuditArchive(db, new Scope(organizationId)).BuildAsync(runId, default, first.CutoffTimestamp, first.CutoffId))!;
            Assert.Equal(first.Bytes, replay.Bytes);
            Assert.Equal(first.AuditJsonSha256, replay.AuditJsonSha256);
        }
        using var zip = new ZipArchive(new MemoryStream(first.Bytes));
        using var reader = new StreamReader(zip.GetEntry("audit.json")!.Open());
        var json = await reader.ReadToEndAsync();
        Assert.Equal(first.AuditJsonSha256, AuditWriter.Sha256(json));
        Assert.DoesNotContain("other-tenant-secret-event", json);
        Assert.DoesNotContain("later-export", json);
        Assert.DoesNotContain("older-unrelated-event", json);
        Assert.DoesNotContain("same-label-current-pcr", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(organizationId, document.RootElement.GetProperty("organizationId").GetGuid());
        Assert.StartsWith("unknown:", document.RootElement.GetProperty("pcrReference").GetString());
        await using var foreign = CreateDb(otherOrganization);
        Assert.Null(await new AuditArchive(foreign, new Scope(otherOrganization)).BuildAsync(runId, default));
    }

    [Fact]
    public async Task MigrationPreservesLegacyEventsAndAllowsOnlyEvidenceSafeRollback()
    {
        var database = "carbon_audit_migration_" + Guid.NewGuid().ToString("N");
        var serverBuilder = new NpgsqlConnectionStringBuilder(AdminConnection()) { Database = "postgres" };
        var fixtureBuilder = new NpgsqlConnectionStringBuilder(AdminConnection()) { Database = database };
        await using var admin = new NpgsqlConnection(serverBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var organizationId = Guid.NewGuid();
            var legacyId = Guid.NewGuid();
            await using var db = CreateDb(organizationId, fixtureBuilder.ConnectionString);
            var migrator = db.GetService<IMigrator>();
            const string previous = "20260907131845_PreserveCanonicalManifestText";
            await migrator.MigrateAsync(previous);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO app.audit_events (id, timestamp, actor_id, organization_id, action, resource_type,
                    resource_id, before_hash, after_hash, correlation_id, metadata_json)
                VALUES ({legacyId}, {DateTimeOffset.UtcNow}, NULL, {organizationId}, 'legacy.fixture', 'Fixture',
                    {legacyId}, NULL, NULL, 'legacy-fixture', CAST({"{}"} AS jsonb))
                """);
            await migrator.MigrateAsync();
            var legacy = await db.AuditEvents.AsNoTracking().SingleAsync(item => item.Id == legacyId);
            Assert.Null(legacy.BeforeHash); Assert.Null(legacy.AfterHash); Assert.Equal("{}", legacy.MetadataJson);
            await migrator.MigrateAsync(previous);
            await migrator.MigrateAsync();
            new AuditWriter(db).Append("fixture.created", "Fixture", Guid.NewGuid(), organizationId, null, null,
                new Dictionary<string, object?> { ["status"] = "Created" }, "Versioned evidence rollback fixture", "versioned-fixture");
            await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.True(await db.AuditEvents.AnyAsync(item => item.Action == "fixture.created"));
            await migrator.MigrateAsync();
        }
        finally
        {
            using var pool = new NpgsqlConnection(fixtureBuilder.ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static string AdminConnection() => Environment.GetEnvironmentVariable("CARBON_TEST_ADMIN_DB_CONNECTION")
        ?? Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION") ?? throw new InvalidOperationException("Isolated audit test database is required.");

    private static CarbonFootprintDbContext CreateDb(Guid organizationId, string? connection = null) => new(
        new DbContextOptionsBuilder<CarbonFootprintDbContext>().UseNpgsql(connection ?? AdminConnection()).UseSnakeCaseNamingConvention().Options,
        new Scope(organizationId));

    private sealed record Scope(Guid Id) : IOrganizationScope { public Guid? OrganizationId => Id; }
}
