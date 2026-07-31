using CarbonFootprint.Application.Factors;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Infrastructure.LegacyImport;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Organizations;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Integration.Tests;

public sealed class PostgreSqlPersistenceTests
{
    [Fact]
    public void Model_HasNoPendingMigrationChanges()
    {
        using var dbContext = CreateContext(Guid.NewGuid());

        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task UnitCatalogueV2_SeedsTransportAndTonneConversions()
    {
        await using var dbContext = CreateContext(Guid.NewGuid());

        var units = await dbContext.Units
            .Where(item => item.CatalogueVersion == "units-p0-v2")
            .OrderBy(item => item.Code)
            .ToArrayAsync();

        Assert.Equal(6, units.Length);
        Assert.Equal(1000m, units.Single(item => item.Code == "tonne").ScaleToCanonical);
        Assert.Equal("transport-work", units.Single(item => item.Code == "tonne-km").Dimension);
        Assert.Equal("count", units.Single(item => item.Code == "piece").Dimension);
    }

    [Fact]
    public async Task QueryFilters_KeepOrganizationsIsolated_AndRejectCrossTenantWrite()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using (var contextA = CreateContext(organizationA))
        {
            contextA.Organizations.Add(new OrganizationRecord
            {
                Id = organizationA,
                Name = "整合測試組織 A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            contextA.Products.Add(new ProductRecord
            {
                Id = productA,
                OrganizationId = organizationA,
                Name = "產品 A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await contextA.SaveChangesAsync();
        }

        await using (var contextB = CreateContext(organizationB))
        {
            contextB.Organizations.Add(new OrganizationRecord
            {
                Id = organizationB,
                Name = "整合測試組織 B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            contextB.Products.Add(new ProductRecord
            {
                Id = productB,
                OrganizationId = organizationB,
                Name = "產品 B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await contextB.SaveChangesAsync();
        }

        await using (var contextA = CreateContext(organizationA))
        {
            Assert.Equal([productA], await contextA.Products.Select(item => item.Id).ToArrayAsync());
            Assert.Null(await contextA.Products.SingleOrDefaultAsync(item => item.Id == productB));

            contextA.Products.Add(new ProductRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                Name = "越權寫入",
                CreatedAt = DateTimeOffset.UtcNow
            });
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => contextA.SaveChangesAsync());
            Assert.Contains("不符合目前組織範圍", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OrganizationMailSettings_AreTenantScoped()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();

        var settingsAId = Guid.NewGuid();
        var settingsBId = Guid.NewGuid();
        await using (var context = CreateContext(organizationA))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationA,
                Name = "SMTP 測試組織 A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationB))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationB,
                Name = "SMTP 測試組織 B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationA))
        {
            context.OrganizationMailSettings.Add(new OrganizationMailSettingsRecord
            {
                Id = settingsAId,
                OrganizationId = organizationA,
                Host = "smtp-a.example.test",
                Port = 587,
                EnableSsl = true,
                FromAddress = "a@example.test",
                FromName = "組織 A",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationB))
        {
            context.OrganizationMailSettings.Add(new OrganizationMailSettingsRecord
            {
                Id = settingsBId,
                OrganizationId = organizationB,
                Host = "smtp-b.example.test",
                Port = 465,
                EnableSsl = true,
                FromAddress = "b@example.test",
                FromName = "組織 B",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationA))
        {
            var settings = await context.OrganizationMailSettings.SingleAsync();
            Assert.Equal("smtp-a.example.test", settings.Host);
            Assert.Empty(settings.EncryptedPassword);
            Assert.Null(await context.OrganizationMailSettings
                .SingleOrDefaultAsync(item => item.Id == settingsBId));

            context.OrganizationMailSettings.Add(new OrganizationMailSettingsRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                Host = "smtp-id-or.example.test",
                Port = 25,
                FromAddress = "id-or@example.test",
                FromName = "越權"
            });
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("不符合目前組織範圍", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task QueryFilters_ResolveOrganizationWhenRequestScopeBecomesAvailable()
    {
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await using (var seededContext = CreateContext(organizationId))
        {
            seededContext.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "延遲租戶測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            seededContext.Products.Add(new ProductRecord
            {
                Id = productId,
                OrganizationId = organizationId,
                Name = "延遲租戶測試產品",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await seededContext.SaveChangesAsync();
        }

        var scope = new MutableOrganizationScope();
        await using var context = CreateContext(scope);
        Assert.Empty(await context.Products.ToArrayAsync());

        scope.OrganizationId = organizationId;
        Assert.Equal([productId], await context.Products.Select(item => item.Id).ToArrayAsync());
    }

    [Fact]
    public async Task LegacyFactorImporter_StagesValidInvalidAndConflictRowsWithoutPublishing()
    {
        var organizationId = Guid.NewGuid();
        var uniqueName = Guid.NewGuid().ToString("N");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"legacy-factors-{uniqueName}.csv");
        await File.WriteAllTextAsync(
            sourcePath,
            $"name,value,denominator_unit,source_version,license_code\n" +
            $"factor-{uniqueName},2.5,kg,dataset-1,fixture\n" +
            $"invalid-{uniqueName},-1,unknown,dataset-1,fixture\n" +
            $"factor-{uniqueName},2.5,kg,dataset-1,fixture\n");

        try
        {
            await using var context = CreateContext(organizationId);
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "Legacy staging 測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();

            var report = await new LegacyFactorCsvImporter(context).ImportAsync(
                organizationId,
                sourcePath,
                CancellationToken.None);

            Assert.Equal(1, report.ParsedRows);
            Assert.Equal(1, report.InvalidRows);
            Assert.Equal(1, report.ConflictRows);
            Assert.Equal(3, await context.LegacyStagingRows.CountAsync());
            Assert.Empty(await context.EmissionFactorVersions.ToArrayAsync());
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task MoenvFactorSynchronization_PublishesOfficialFactorsWithoutReview()
    {
        var organizationId = Guid.NewGuid();
        await using (var context = CreateContext(organizationId))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "部署係數匯入測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                FactorId = Guid.NewGuid(),
                VersionNumber = 1,
                Name = "測試天然氣",
                Value = 2.5m,
                NumeratorUnitCode = "kgCO2e",
                DenominatorUnitCode = "kg",
                Geography = "TW",
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = null,
                PublicationStatus = FactorPublicationStatus.Draft.ToString(),
                SourceDatasetVersion = "CFP_P_02-2026",
                LicenseCode = "政府資料開放授權條款第1版",
                SourceType = "government-database",
                SourceName = "環境部",
                SourceReference = MoenvFactorClient.DatasetReference,
                DatasetName = "環境部碳足跡排放係數",
                OriginalDocumentName = "CFP_P_02-record-aaaaaaaaaaaa.json",
                OriginalDocumentSha256 = new string('a', 64),
                Applicability = "測試適用性",
                ReviewStatus = FactorReviewStatus.Pending.ToString()
            });
            await context.SaveChangesAsync();
        }

        var source = new StubMoenvFactorSource(new MoenvFactorDownload(
            [
                new MoenvFactorRecord(
                    "測試電力",
                    0.5m,
                    "kWh",
                    "環境部",
                    2026,
                    new string('a', 64)),
                new MoenvFactorRecord(
                    "測試天然氣",
                    2.5m,
                    "kg",
                    "環境部",
                    2026,
                    new string('a', 64))
            ],
            2));
        var service = new MoenvFactorSynchronizationService(CreateOptions(), source);

        var first = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "deployment-test",
            CancellationToken.None);
        var second = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "deployment-test",
            CancellationToken.None);

        Assert.Equal(1, first.CreatedCount);
        Assert.Equal(0, first.UnchangedCount);
        Assert.Equal(1, first.PublishedExistingCount);
        Assert.Equal(2, first.SkippedCount);
        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(2, second.UnchangedCount);
        Assert.Equal(0, second.PublishedExistingCount);

        await using var verification = CreateContext(organizationId);
        var factors = await verification.EmissionFactorVersions
            .Where(item => item.SourceReference == MoenvFactorClient.DatasetReference)
            .OrderBy(item => item.Name)
            .ToArrayAsync();
        Assert.Equal(2, factors.Length);
        Assert.All(factors, factor =>
        {
            Assert.Equal(FactorPublicationStatus.Published.ToString(), factor.PublicationStatus);
            Assert.Equal(FactorReviewStatus.NotRequired.ToString(), factor.ReviewStatus);
            Assert.Null(factor.ReviewedAt);
            Assert.NotNull(factor.PublishedAt);
        });
        var factor = factors.Single(item => item.Name == "測試電力");
        var audit = await verification.AuditEvents.SingleAsync(
            item => item.Action == "factor.version.synced" && item.ResourceId == factor.Id);
        Assert.Null(audit.ActorId);
        Assert.Equal("deployment-test", audit.CorrelationId);
        Assert.Single(await verification.AuditEvents
            .Where(item => item.Action == "factor.version.auto-published")
            .ToArrayAsync());
        var synchronizationAudits = await verification.AuditEvents
            .Where(item =>
                item.Action == "factor.synchronization.completed"
                && item.ResourceId == organizationId)
            .OrderBy(item => item.Timestamp)
            .ToArrayAsync();
        Assert.Equal(2, synchronizationAudits.Length);
        Assert.All(synchronizationAudits, item =>
        {
            Assert.Null(item.ActorId);
            Assert.Equal("deployment-test", item.CorrelationId);
            Assert.Contains(MoenvFactorClient.DatasetReference, item.MetadataJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task MoenvFactorSynchronization_DoesNotAutoPublishManualOrWithdrawnVersions()
    {
        var organizationId = Guid.NewGuid();
        var manualFactorId = Guid.NewGuid();
        var withdrawnFactorId = Guid.NewGuid();
        await using (var context = CreateContext(organizationId))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "同步來源辨識測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.EmissionFactorVersions.AddRange(
                CreateFactorVersion(
                    organizationId,
                    manualFactorId,
                    "手動來源係數",
                    1.5m,
                    FactorPublicationStatus.Draft,
                    FactorReviewStatus.Pending,
                    MoenvFactorClient.DatasetReference,
                    "manual-source.pdf",
                    new string('c', 64)),
                CreateFactorVersion(
                    organizationId,
                    withdrawnFactorId,
                    "已撤回官方係數",
                    3.5m,
                    FactorPublicationStatus.Withdrawn,
                    FactorReviewStatus.NotRequired,
                    MoenvFactorClient.DatasetReference,
                    "CFP_P_02-record-dddddddddddd.json",
                    new string('d', 64)));
            await context.SaveChangesAsync();
        }

        var service = new MoenvFactorSynchronizationService(
            CreateOptions(),
            new StubMoenvFactorSource(new MoenvFactorDownload(
                [
                    new MoenvFactorRecord(
                        "手動來源係數",
                        1.5m,
                        "kg",
                        "環境部",
                        2026,
                        new string('c', 64)),
                    new MoenvFactorRecord(
                        "已撤回官方係數",
                        3.5m,
                        "kg",
                        "環境部",
                        2026,
                        new string('d', 64))
                ],
                0)));

        var result = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "source-classification-test",
            CancellationToken.None);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(0, result.PublishedExistingCount);
        await using var verification = CreateContext(organizationId);
        var manual = await verification.EmissionFactorVersions.SingleAsync(item => item.FactorId == manualFactorId);
        Assert.Equal(FactorPublicationStatus.Draft.ToString(), manual.PublicationStatus);
        Assert.Equal(FactorReviewStatus.Pending.ToString(), manual.ReviewStatus);
        var synchronized = await verification.EmissionFactorVersions.SingleAsync(item =>
            item.Name == "手動來源係數" && item.FactorId != manualFactorId);
        Assert.Equal(FactorPublicationStatus.Published.ToString(), synchronized.PublicationStatus);
        Assert.Equal(FactorReviewStatus.NotRequired.ToString(), synchronized.ReviewStatus);
        var withdrawn = await verification.EmissionFactorVersions.SingleAsync(item => item.FactorId == withdrawnFactorId);
        Assert.Equal(FactorPublicationStatus.Withdrawn.ToString(), withdrawn.PublicationStatus);
    }

    [Fact]
    public async Task MoenvFactorSynchronization_UsesNextVersionAcrossManualAndSynchronizedSources()
    {
        var organizationId = Guid.NewGuid();
        var factorId = Guid.NewGuid();
        var synchronizedVersionId = Guid.NewGuid();
        var manualVersionId = Guid.NewGuid();
        await using (var context = CreateContext(organizationId))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "同步跨來源版號測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            var synchronizedVersion = CreateFactorVersion(
                organizationId,
                factorId,
                "跨來源版號係數",
                1m,
                FactorPublicationStatus.Withdrawn,
                FactorReviewStatus.NotRequired,
                MoenvFactorClient.DatasetReference,
                "CFP_P_02-record-eeeeeeeeeeee.json",
                new string('e', 64));
            synchronizedVersion.Id = synchronizedVersionId;
            synchronizedVersion.SourceDatasetVersion = "CFP_P_02-2025";
            var manualVersion = CreateFactorVersion(
                organizationId,
                factorId,
                "跨來源版號係數",
                1.1m,
                FactorPublicationStatus.Published,
                FactorReviewStatus.Approved,
                "manual-reference",
                "manual-update.pdf",
                string.Empty);
            manualVersion.Id = manualVersionId;
            manualVersion.VersionNumber = 2;
            manualVersion.SupersedesVersionId = synchronizedVersionId;
            context.EmissionFactorVersions.AddRange(synchronizedVersion, manualVersion);
            await context.SaveChangesAsync();
        }

        var service = new MoenvFactorSynchronizationService(
            CreateOptions(),
            new StubMoenvFactorSource(new MoenvFactorDownload(
                [
                    new MoenvFactorRecord(
                        "跨來源版號係數",
                        1.2m,
                        "kg",
                        "環境部",
                        2026,
                        new string('f', 64))
                ],
                0)));

        var result = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "cross-source-version-test",
            CancellationToken.None);

        Assert.Equal(1, result.CreatedCount);
        await using var verification = CreateContext(organizationId);
        var versions = await verification.EmissionFactorVersions
            .Where(item => item.FactorId == factorId)
            .OrderBy(item => item.VersionNumber)
            .ToArrayAsync();
        Assert.Equal([1, 2, 3], versions.Select(item => item.VersionNumber));
        Assert.Equal(FactorPublicationStatus.Withdrawn.ToString(), versions[1].PublicationStatus);
        Assert.Equal(FactorPublicationStatus.Published.ToString(), versions[2].PublicationStatus);
        Assert.Equal(FactorReviewStatus.NotRequired.ToString(), versions[2].ReviewStatus);
        Assert.Equal(manualVersionId, versions[2].SupersedesVersionId);
    }

    [Fact]
    public async Task OrganizationInvitation_RequiresMatchingEmail_AndCreatesScopedMembership()
    {
        var organizationId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var inviteeEmail = $"invitee-{inviteeId:N}@example.test";
        await using (var context = CreateContext(organizationId))
        {
            context.Users.AddRange(
                new ApplicationUser
                {
                    Id = ownerId,
                    UserName = $"owner-{ownerId:N}@example.test",
                    NormalizedUserName = $"OWNER-{ownerId:N}@EXAMPLE.TEST"
                },
                new ApplicationUser
                {
                    Id = inviteeId,
                    UserName = inviteeEmail,
                    NormalizedUserName = inviteeEmail.ToUpperInvariant(),
                    Email = inviteeEmail,
                    NormalizedEmail = inviteeEmail.ToUpperInvariant()
                });
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "Invitation integration organization",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var service = new OrganizationInvitationService(CreateOptions());
        var token = await service.CreateAsync(
            organizationId,
            ownerId,
            inviteeEmail,
            OrganizationRole.Contributor,
            CancellationToken.None);
        var wrongUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "wrong@example.test",
            NormalizedEmail = "WRONG@EXAMPLE.TEST"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptAsync(wrongUser, token, CancellationToken.None));

        var invitee = new ApplicationUser
        {
            Id = inviteeId,
            Email = inviteeEmail,
            NormalizedEmail = inviteeEmail.ToUpperInvariant()
        };
        Assert.Equal(organizationId, await service.AcceptAsync(invitee, token, CancellationToken.None));

        await using var verification = CreateContext(organizationId);
        var membership = await verification.OrganizationMemberships.SingleAsync(item => item.UserId == inviteeId);
        Assert.Equal(OrganizationRole.Contributor.ToString(), membership.Role);
        Assert.NotNull((await verification.OrganizationInvitations.SingleAsync()).AcceptedAt);
        Assert.True(await verification.UserClaims.AnyAsync(item =>
            item.UserId == inviteeId && item.ClaimType == "organization_id" && item.ClaimValue == organizationId.ToString()));
    }

    [Fact]
    public async Task PublishedPcr_ContentAndStageRulesAreImmutable()
    {
        var organizationId = Guid.NewGuid();
        var pcr = CreatePcrVersion(organizationId, PcrPublicationStatus.Published);
        var stageRule = new PcrStageRuleRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PcrVersionId = pcr.Id,
            LifecycleStage = 1,
            Requirement = PcrStageRequirement.Mandatory.ToString(),
            PermittedActivityKindsCsv = "Material,MaterialTransport",
            RequiredFieldsCsv = "SourceReference"
        };

        await using (var setup = CreateContext(organizationId))
        {
            setup.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "PCR immutability organization",
                CreatedAt = DateTimeOffset.UtcNow
            });
            setup.PcrVersions.Add(pcr);
            setup.PcrStageRules.Add(stageRule);
            await setup.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationId))
        {
            var published = await context.PcrVersions.SingleAsync(item => item.Id == pcr.Id);
            published.Title = "不可覆寫的名稱";
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("請建立新版本", exception.Message, StringComparison.Ordinal);
        }

        await using (var context = CreateContext(organizationId))
        {
            var rule = await context.PcrStageRules.SingleAsync(item => item.Id == stageRule.Id);
            rule.Requirement = PcrStageRequirement.Optional.ToString();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("階段規則不可修改", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PcrStageRules_AreTenantScoped()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var pcrA = CreatePcrVersion(organizationA, PcrPublicationStatus.Draft);
        var pcrB = CreatePcrVersion(organizationB, PcrPublicationStatus.Draft);

        await using (var context = CreateContext(organizationA))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationA,
                Name = "PCR tenant A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.PcrVersions.Add(pcrA);
            context.PcrStageRules.Add(new PcrStageRuleRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationA,
                PcrVersionId = pcrA.Id,
                LifecycleStage = 1,
                Requirement = PcrStageRequirement.Optional.ToString()
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationB))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationB,
                Name = "PCR tenant B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.PcrVersions.Add(pcrB);
            context.PcrStageRules.Add(new PcrStageRuleRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                PcrVersionId = pcrB.Id,
                LifecycleStage = 1,
                Requirement = PcrStageRequirement.Optional.ToString()
            });
            await context.SaveChangesAsync();
        }

        await using var verification = CreateContext(organizationA);
        Assert.Equal([pcrA.Id], await verification.PcrStageRules.Select(item => item.PcrVersionId).ToArrayAsync());
        Assert.Null(await verification.PcrStageRules.SingleOrDefaultAsync(item => item.PcrVersionId == pcrB.Id));
    }


    [Fact]
    public async Task GovernanceRecords_AreTenantScoped_AndImmutableVersionsCannotBeOverwritten()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var projectA = await SeedGovernanceProjectAsync(organizationA, "A");
        var projectB = await SeedGovernanceProjectAsync(organizationB, "B");
        var recordAId = Guid.NewGuid();
        var recordBId = Guid.NewGuid();

        await using (var context = CreateContext(organizationA))
        {
            context.ProjectGovernanceRecords.Add(new ProjectGovernanceRecord
            {
                Id = recordAId,
                OrganizationId = organizationA,
                ProjectVersionId = projectA,
                TargetEntityId = projectA,
                RecordType = GovernanceRecordTypes.ReadinessReport,
                StableKey = "latest",
                VersionNumber = 1,
                Status = "Passed",
                PayloadJson = "{}",
                CanonicalSha256 = new string('a', 64),
                IsImmutable = true,
                CreatedAt = DateTimeOffset.UtcNow,
                LockedAt = DateTimeOffset.UtcNow,
                LockReason = "integration-test"
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(organizationB))
        {
            context.ProjectGovernanceRecords.Add(new ProjectGovernanceRecord
            {
                Id = recordBId,
                OrganizationId = organizationB,
                ProjectVersionId = projectB,
                TargetEntityId = projectB,
                RecordType = GovernanceRecordTypes.ReadinessReport,
                StableKey = "latest",
                VersionNumber = 1,
                Status = "Passed",
                PayloadJson = "{}",
                CanonicalSha256 = new string('b', 64),
                IsImmutable = true,
                CreatedAt = DateTimeOffset.UtcNow,
                LockedAt = DateTimeOffset.UtcNow,
                LockReason = "integration-test"
            });
            await context.SaveChangesAsync();
        }

        await using (var verification = CreateContext(organizationA))
        {
            Assert.Equal([recordAId], await verification.ProjectGovernanceRecords.Select(item => item.Id).ToArrayAsync());
            Assert.Null(await verification.ProjectGovernanceRecords.SingleOrDefaultAsync(item => item.Id == recordBId));

            var immutable = await verification.ProjectGovernanceRecords.SingleAsync(item => item.Id == recordAId);
            immutable.PayloadJson = "{\"changed\":true}";
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => verification.SaveChangesAsync());
            Assert.Contains("不可修改", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PublishedGovernanceDefinition_IsVersionLocked_AndGlobalDefinitionsRemainReadable()
    {
        var organizationId = Guid.NewGuid();
        var organizationDefinitionKey = $"org-formula-{Guid.NewGuid():N}";
        var globalDefinitionKey = $"global-{Guid.NewGuid():N}";
        await using (var context = CreateContext(organizationId))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "Governance definition organization",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.GovernanceDefinitions.AddRange(
                new GovernanceDefinitionRecord
                {
                    Id = Guid.NewGuid(),
                    DefinitionId = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    DefinitionType = GovernanceDefinitionTypes.ActivityFormula,
                    StableKey = organizationDefinitionKey,
                    VersionNumber = 1,
                    Name = "Organization formula",
                    PublicationStatus = "Published",
                    PayloadJson = "{}",
                    CanonicalSha256 = new string('c', 64),
                    CreatedAt = DateTimeOffset.UtcNow,
                    PublishedAt = DateTimeOffset.UtcNow
                },
                new GovernanceDefinitionRecord
                {
                    Id = Guid.NewGuid(),
                    DefinitionId = Guid.NewGuid(),
                    OrganizationId = null,
                    DefinitionType = GovernanceDefinitionTypes.GlobalEmissionFactor,
                    StableKey = globalDefinitionKey,
                    VersionNumber = 1,
                    Name = "Global factor",
                    PublicationStatus = "Published",
                    PayloadJson = "{}",
                    CanonicalSha256 = new string('d', 64),
                    CreatedAt = DateTimeOffset.UtcNow,
                    PublishedAt = DateTimeOffset.UtcNow
                });
            await context.SaveChangesAsync();
        }

        await using var verification = CreateContext(organizationId);
        var visibleDefinitionKeys = await verification.GovernanceDefinitions
            .Where(item => item.StableKey == organizationDefinitionKey || item.StableKey == globalDefinitionKey)
            .Select(item => item.StableKey)
            .OrderBy(item => item)
            .ToArrayAsync();
        Assert.Equal(
            new[] { globalDefinitionKey, organizationDefinitionKey }.OrderBy(item => item),
            visibleDefinitionKeys);
        var definition = await verification.GovernanceDefinitions
            .SingleAsync(item => item.StableKey == organizationDefinitionKey);
        definition.Name = "Illegal overwrite";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => verification.SaveChangesAsync());
        Assert.Contains("請建立新版本", exception.Message, StringComparison.Ordinal);
    }


    private static async Task<Guid> SeedGovernanceProjectAsync(Guid organizationId, string suffix)
    {
        var productId = Guid.NewGuid();
        var productVersionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        await using var context = CreateContext(organizationId);
        context.Organizations.Add(new OrganizationRecord
        {
            Id = organizationId,
            Name = $"Governance organization {suffix}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        context.Products.Add(new ProductRecord
        {
            Id = productId,
            OrganizationId = organizationId,
            Name = $"Governance product {suffix}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        context.ProductVersions.Add(new ProductVersionRecord
        {
            Id = productVersionId,
            OrganizationId = organizationId,
            ProductId = productId,
            VersionNumber = 1,
            NameZhTw = $"治理產品 {suffix}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        context.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
        {
            Id = projectId,
            OrganizationId = organizationId,
            ProductVersionId = productVersionId,
            VersionNumber = 1,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            FunctionalUnit = "1 item",
            DeclaredUnit = "piece",
            SystemBoundary = "cradle-to-grave",
            AllocationMethod = "mass",
            PcrVersion = "integration",
            WorkflowStatus = "Draft",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        return projectId;
    }

    private static CarbonFootprintDbContext CreateContext(Guid organizationId)
        => CreateContext(new TestOrganizationScope(organizationId));

    private static CarbonFootprintDbContext CreateContext(IOrganizationScope organizationScope)
    {
        return new CarbonFootprintDbContext(CreateOptions(), organizationScope);
    }

    private static DbContextOptions<CarbonFootprintDbContext> CreateOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Integration test 需要 CARBON_TEST_DB_CONNECTION。");
        return new DbContextOptionsBuilder<CarbonFootprintDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
    }

    private sealed record TestOrganizationScope(Guid Value) : IOrganizationScope
    {
        public Guid? OrganizationId => Value;
    }

    private sealed class MutableOrganizationScope : IOrganizationScope
    {
        public Guid? OrganizationId { get; set; }
    }

    private sealed class StubMoenvFactorSource(MoenvFactorDownload download) : IMoenvFactorSource
    {
        public Task<MoenvFactorDownload> DownloadAsync(CancellationToken cancellationToken)
            => Task.FromResult(download);
    }

    private static EmissionFactorVersionRecord CreateFactorVersion(
        Guid organizationId,
        Guid factorId,
        string name,
        decimal value,
        FactorPublicationStatus publicationStatus,
        FactorReviewStatus reviewStatus,
        string sourceReference,
        string originalDocumentName,
        string originalDocumentSha256) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FactorId = factorId,
            VersionNumber = 1,
            Name = name,
            Value = value,
            NumeratorUnitCode = "kgCO2e",
            DenominatorUnitCode = "kg",
            Geography = "TW",
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = null,
            PublicationStatus = publicationStatus.ToString(),
            SourceDatasetVersion = "CFP_P_02-2026",
            LicenseCode = "政府資料開放授權條款第1版",
            SourceType = "government-database",
            SourceName = "環境部",
            SourceReference = sourceReference,
            DatasetName = "環境部碳足跡排放係數",
            OriginalDocumentName = originalDocumentName,
            OriginalDocumentSha256 = originalDocumentSha256,
            Applicability = "測試適用性",
            ReviewStatus = reviewStatus.ToString()
        };

    private static PcrVersionRecord CreatePcrVersion(
        Guid organizationId,
        PcrPublicationStatus publicationStatus) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RuleSetId = Guid.NewGuid(),
            RegistrationNumber = $"PCR-{Guid.NewGuid():N}",
            VersionNumber = 1,
            Title = "PCR integration rule",
            ApprovalDate = new DateOnly(2026, 1, 1),
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2027, 12, 31),
            PublicationStatus = publicationStatus.ToString(),
            SourceReference = "https://example.test/pcr",
            StandardCode = "ISO 14067",
            CccClassification = "TEST",
            Applicability = "Integration test",
            RuleRequirements = "Test requirements",
            OriginalDocumentName = "pcr.pdf",
            OriginalDocumentObjectKey = $"test/{Guid.NewGuid():N}",
            OriginalDocumentContentType = "application/pdf",
            OriginalDocumentSizeBytes = 100,
            OriginalDocumentSha256 = new string('a', 64),
            OriginalDocumentScanStatus = "Clean",
            ProductCategoryPatterns = "*",
            FunctionalUnitPattern = "*",
            DeclaredUnitCode = "*",
            SystemBoundaryCode = "*",
            FormulaRuleSetVersion = "test-v1",
            ReportingRequirements = "Test reporting",
            ReviewStatus = PcrReviewStatus.Approved.ToString(),
            CustomApprovalStatus = PcrCustomApprovalStatus.NotRequired.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            PublishedAt = publicationStatus == PcrPublicationStatus.Published
                ? DateTimeOffset.UtcNow
                : null
        };
}
