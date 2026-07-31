using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Persistence;

public sealed class CarbonFootprintDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    private readonly IOrganizationScope _organizationScope;

    public CarbonFootprintDbContext(
        DbContextOptions<CarbonFootprintDbContext> options,
        IOrganizationScope organizationScope)
        : base(options)
    {
        _organizationScope = organizationScope;
    }

    public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();
    public DbSet<OrganizationMailSettingsRecord> OrganizationMailSettings => Set<OrganizationMailSettingsRecord>();
    public DbSet<OrganizationMembershipRecord> OrganizationMemberships => Set<OrganizationMembershipRecord>();
    public DbSet<OrganizationInvitationRecord> OrganizationInvitations => Set<OrganizationInvitationRecord>();
    public DbSet<FacilityRecord> Facilities => Set<FacilityRecord>();
    public DbSet<ProductRecord> Products => Set<ProductRecord>();
    public DbSet<ProductVersionRecord> ProductVersions => Set<ProductVersionRecord>();
    public DbSet<InventoryProjectVersionRecord> InventoryProjectVersions => Set<InventoryProjectVersionRecord>();
    public DbSet<PcrVersionRecord> PcrVersions => Set<PcrVersionRecord>();
    public DbSet<PcrStageRuleRecord> PcrStageRules => Set<PcrStageRuleRecord>();
    public DbSet<LifecycleStageDeclarationRecord> LifecycleStageDeclarations => Set<LifecycleStageDeclarationRecord>();
    public DbSet<UnitRecord> Units => Set<UnitRecord>();
    public DbSet<EmissionFactorVersionRecord> EmissionFactorVersions => Set<EmissionFactorVersionRecord>();
    public DbSet<ActivityDataRecord> ActivityData => Set<ActivityDataRecord>();
    public DbSet<EvidenceFileRecord> EvidenceFiles => Set<EvidenceFileRecord>();
    public DbSet<CalculationRunRecord> CalculationRuns => Set<CalculationRunRecord>();
    public DbSet<CalculationLineRecord> CalculationLineItems => Set<CalculationLineRecord>();
    public DbSet<CalculationStageSummaryRecord> CalculationStageSummaries => Set<CalculationStageSummaryRecord>();
    public DbSet<CalculationWarningRecord> CalculationWarnings => Set<CalculationWarningRecord>();
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();
    public DbSet<LegacyImportBatchRecord> LegacyImportBatches => Set<LegacyImportBatchRecord>();
    public DbSet<LegacyStagingRowRecord> LegacyStagingRows => Set<LegacyStagingRowRecord>();
    public DbSet<LegacyImportConflictRecord> LegacyImportConflicts => Set<LegacyImportConflictRecord>();
    public DbSet<GovernanceDefinitionRecord> GovernanceDefinitions => Set<GovernanceDefinitionRecord>();
    public DbSet<OrganizationDefinitionActivationRecord> OrganizationDefinitionActivations => Set<OrganizationDefinitionActivationRecord>();
    public DbSet<ProjectGovernanceRecord> ProjectGovernanceRecords => Set<ProjectGovernanceRecord>();
    public DbSet<GovernanceEventRecord> GovernanceEvents => Set<GovernanceEventRecord>();
    public DbSet<EvidenceDocumentRecord> EvidenceDocuments => Set<EvidenceDocumentRecord>();
    public DbSet<EvidenceDocumentVersionRecord> EvidenceDocumentVersions => Set<EvidenceDocumentVersionRecord>();
    public DbSet<EvidenceLinkRecord> EvidenceLinks => Set<EvidenceLinkRecord>();
    public DbSet<EvidenceAccessLogRecord> EvidenceAccessLogs => Set<EvidenceAccessLogRecord>();
    public DbSet<EvidenceRetentionLockRecord> EvidenceRetentionLocks => Set<EvidenceRetentionLockRecord>();
    public DbSet<VerificationArchiveRecord> VerificationArchives => Set<VerificationArchiveRecord>();
    public DbSet<ProjectImpactRecord> ProjectImpacts => Set<ProjectImpactRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("app");
        ConfigureIdentity(builder);
        ConfigureOrganizations(builder);
        ConfigureProducts(builder);
        ConfigureInventories(builder);
        ConfigureUnitsAndFactors(builder);
        ConfigureCalculations(builder);
        ConfigureAudit(builder);
        ConfigureGovernance(builder);
        ConfigureLegacyStaging(builder);
        ConfigureTenantFilters(builder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().ToTable("users", "identity");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles", "identity");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", "identity");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", "identity");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", "identity");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", "identity");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", "identity");
        builder.Entity<IdentityUserLogin<Guid>>().Property(item => item.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<Guid>>().Property(item => item.ProviderKey).HasMaxLength(128);
        builder.Entity<IdentityUserToken<Guid>>().Property(item => item.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserToken<Guid>>().Property(item => item.Name).HasMaxLength(128);
    }

    private static void ConfigureOrganizations(ModelBuilder builder)
    {
        builder.Entity<OrganizationRecord>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200);
        });
        builder.Entity<OrganizationMailSettingsRecord>(entity =>
        {
            entity.ToTable("organization_mail_settings");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Host).HasMaxLength(300);
            entity.Property(item => item.Username).HasMaxLength(320);
            entity.Property(item => item.EncryptedPassword).HasMaxLength(4000);
            entity.Property(item => item.FromAddress).HasMaxLength(320);
            entity.Property(item => item.FromName).HasMaxLength(200);
            entity.HasIndex(item => item.OrganizationId).IsUnique();
            entity.HasOne<OrganizationRecord>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<OrganizationMembershipRecord>(entity =>
        {
            entity.ToTable("organization_memberships");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Role).HasMaxLength(50);
            entity.HasIndex(item => new { item.OrganizationId, item.UserId }).IsUnique();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OrganizationRecord>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<OrganizationInvitationRecord>(entity =>
        {
            entity.ToTable("organization_invitations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Email).HasMaxLength(320);
            entity.Property(item => item.Role).HasMaxLength(50);
            entity.Property(item => item.TokenSha256).HasMaxLength(64);
            entity.HasIndex(item => item.TokenSha256).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.Email });
            entity.HasOne<OrganizationRecord>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.InvitedBy).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<FacilityRecord>(entity =>
        {
            entity.ToTable("facilities");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Code).HasMaxLength(100);
            entity.Property(item => item.Name).HasMaxLength(300);
            entity.HasIndex(item => new { item.OrganizationId, item.Code }).IsUnique();
            entity.HasOne<OrganizationRecord>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureProducts(ModelBuilder builder)
    {
        builder.Entity<ProductRecord>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(300);
            entity.Property(item => item.CategoryCode).HasMaxLength(100);
            entity.HasOne<OrganizationRecord>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FacilityRecord>().WithMany().HasForeignKey(item => item.FacilityId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ProductVersionRecord>(entity =>
        {
            entity.ToTable("product_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.NameZhTw).HasMaxLength(300);
            entity.HasIndex(item => new { item.ProductId, item.VersionNumber }).IsUnique();
            entity.HasOne<ProductRecord>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureInventories(ModelBuilder builder)
    {
        builder.Entity<InventoryProjectVersionRecord>(entity =>
        {
            entity.ToTable("inventory_project_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.FunctionalUnit).HasMaxLength(200);
            entity.Property(item => item.DeclaredUnit).HasMaxLength(200);
            entity.Property(item => item.SystemBoundary).HasMaxLength(1000);
            entity.Property(item => item.AllocationMethod).HasMaxLength(200);
            entity.Property(item => item.AllocationReason).HasMaxLength(2000);
            entity.Property(item => item.Exclusions).HasMaxLength(4000);
            entity.Property(item => item.Assumptions).HasMaxLength(4000);
            entity.Property(item => item.EstimationReason).HasMaxLength(4000);
            entity.Property(item => item.PcrVersion).HasMaxLength(200);
            entity.Property(item => item.WorkflowStatus).HasMaxLength(50);
            entity.Property(item => item.ReviewComment).HasMaxLength(2000);
            entity.HasIndex(item => new { item.ProductVersionId, item.VersionNumber }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.WorkflowStatus });
            entity.HasIndex(item => item.PcrVersionId);
            entity.HasOne<ProductVersionRecord>().WithMany().HasForeignKey(item => item.ProductVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PcrVersionRecord>().WithMany().HasForeignKey(item => item.PcrVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PcrVersionRecord>(entity =>
        {
            entity.ToTable("pcr_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.RegistrationNumber).HasMaxLength(100);
            entity.Property(item => item.Title).HasMaxLength(300);
            entity.Property(item => item.PublicationStatus).HasMaxLength(30);
            entity.Property(item => item.SourceReference).HasMaxLength(500);
            entity.Property(item => item.StandardCode).HasMaxLength(100);
            entity.Property(item => item.CccClassification).HasMaxLength(100);
            entity.Property(item => item.Applicability).HasMaxLength(2000);
            entity.Property(item => item.RuleRequirements).HasMaxLength(4000);
            entity.Property(item => item.OriginalDocumentName).HasMaxLength(300);
            entity.Property(item => item.OriginalDocumentObjectKey).HasMaxLength(1000);
            entity.Property(item => item.OriginalDocumentContentType).HasMaxLength(200);
            entity.Property(item => item.OriginalDocumentSha256).HasMaxLength(64);
            entity.Property(item => item.OriginalDocumentScanStatus).HasMaxLength(30);
            entity.Property(item => item.ProductCategoryPatterns).HasMaxLength(1000);
            entity.Property(item => item.FunctionalUnitPattern).HasMaxLength(500);
            entity.Property(item => item.DeclaredUnitCode).HasMaxLength(50);
            entity.Property(item => item.SystemBoundaryCode).HasMaxLength(200);
            entity.Property(item => item.PermittedAllocationMethodsCsv).HasMaxLength(1000);
            entity.Property(item => item.CutoffThresholdPercent).HasPrecision(9, 6);
            entity.Property(item => item.FormulaRuleSetVersion).HasMaxLength(200);
            entity.Property(item => item.ReportingRequirements).HasMaxLength(4000);
            entity.Property(item => item.CustomRuleJustification).HasMaxLength(4000);
            entity.Property(item => item.CustomApprovalStatus).HasMaxLength(30);
            entity.Property(item => item.DeprecationReason).HasMaxLength(2000);
            entity.Property(item => item.ReviewStatus).HasMaxLength(30);
            entity.HasIndex(item => new { item.OrganizationId, item.RegistrationNumber, item.VersionNumber }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.RuleSetId, item.VersionNumber }).IsUnique();
            entity.HasOne<PcrVersionRecord>()
                .WithMany()
                .HasForeignKey(item => item.SupersedesVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(item => item.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PcrStageRuleRecord>(entity =>
        {
            entity.ToTable("pcr_stage_rules");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Requirement).HasMaxLength(30);
            entity.Property(item => item.PermittedActivityKindsCsv).HasMaxLength(1000);
            entity.Property(item => item.RequiredFieldsCsv).HasMaxLength(1000);
            entity.HasIndex(item => new { item.PcrVersionId, item.LifecycleStage }).IsUnique();
            entity.HasOne<PcrVersionRecord>()
                .WithMany()
                .HasForeignKey(item => item.PcrVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ActivityDataRecord>(entity =>
        {
            entity.ToTable("activity_data_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(300);
            entity.Property(item => item.ActivityKind).HasMaxLength(100);
            entity.Property(item => item.SupplierOrScenario).HasMaxLength(1000);
            entity.Property(item => item.EquipmentCategory).HasMaxLength(200);
            entity.Property(item => item.DataSourceType).HasMaxLength(200);
            entity.Property(item => item.DataProvider).HasMaxLength(300);
            entity.Property(item => item.CollectionMethod).HasMaxLength(300);
            entity.Property(item => item.SourceReference).HasMaxLength(500);
            entity.Property(item => item.RawValue).HasPrecision(30, 12);
            entity.Property(item => item.CanonicalValue).HasPrecision(30, 12);
            entity.Property(item => item.RawUnitCode).HasMaxLength(50);
            entity.Property(item => item.CanonicalUnitCode).HasMaxLength(50);
            entity.Property(item => item.ConversionRuleVersion).HasMaxLength(100);
            entity.Property(item => item.AmountFormulaId).HasMaxLength(150);
            entity.Property(item => item.FormulaInputsJson).HasColumnType("jsonb");
            entity.Property(item => item.FormulaTraceJson).HasColumnType("jsonb");
            entity.Property(item => item.GovernanceTraceJson).HasColumnType("jsonb");
            entity.Property(item => item.EvidenceSha256).HasMaxLength(64);
            entity.Property(item => item.AllocationFactor).HasPrecision(18, 15);
            entity.Property(item => item.EstimationReason).HasMaxLength(4000);
            entity.Property(item => item.DataQuality).HasMaxLength(100);
            entity.HasOne<InventoryProjectVersionRecord>().WithMany().HasForeignKey(item => item.InventoryProjectVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EmissionFactorVersionRecord>().WithMany().HasForeignKey(item => item.FactorVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GovernanceDefinitionRecord>().WithMany().HasForeignKey(item => item.GlobalFactorDefinitionVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GovernanceDefinitionRecord>().WithMany().HasForeignKey(item => item.FormulaDefinitionVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectGovernanceRecord>().WithMany().HasForeignKey(item => item.DataQualityGovernanceRecordId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectGovernanceRecord>().WithMany().HasForeignKey(item => item.AllocationGovernanceRecordId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectGovernanceRecord>().WithMany().HasForeignKey(item => item.TransportGovernanceRecordId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LifecycleStageDeclarationRecord>(entity =>
        {
            entity.ToTable("lifecycle_stage_declarations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Reason).HasMaxLength(2000);
            entity.HasIndex(item => new { item.InventoryProjectVersionId, item.LifecycleStage }).IsUnique();
            entity.HasOne<InventoryProjectVersionRecord>().WithMany().HasForeignKey(item => item.InventoryProjectVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EvidenceFileRecord>(entity =>
        {
            entity.ToTable("evidence_files");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ObjectKey).HasMaxLength(500);
            entity.Property(item => item.OriginalFileName).HasMaxLength(300);
            entity.Property(item => item.ContentType).HasMaxLength(200);
            entity.Property(item => item.Sha256).HasMaxLength(64);
            entity.Property(item => item.ScanStatus).HasMaxLength(30);
            entity.HasIndex(item => new { item.OrganizationId, item.Sha256 });
            entity.HasOne<ActivityDataRecord>().WithMany().HasForeignKey(item => item.ActivityDataId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureUnitsAndFactors(ModelBuilder builder)
    {
        builder.Entity<UnitRecord>(entity =>
        {
            entity.ToTable("units");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ScaleToCanonical).HasPrecision(30, 15);
            entity.Property(item => item.OffsetToCanonical).HasPrecision(30, 15);
            entity.Property(item => item.AliasesCsv).HasMaxLength(500);
            entity.Property(item => item.CompositeExpression).HasMaxLength(200);
            entity.HasIndex(item => new { item.Code, item.CatalogueVersion }).IsUnique();
            entity.HasData(
                new UnitRecord { Id = Guid.Parse("71000000-0000-0000-0000-000000000001"), Code = "kg", Symbol = "kg", Dimension = "mass", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "kg", CatalogueVersion = "units-p0-v1", AliasesCsv = "kilogram,kilograms" },
                new UnitRecord { Id = Guid.Parse("71000000-0000-0000-0000-000000000002"), Code = "g", Symbol = "g", Dimension = "mass", ScaleToCanonical = 0.001m, OffsetToCanonical = 0m, CanonicalCode = "kg", CatalogueVersion = "units-p0-v1", AliasesCsv = "gram,grams" },
                new UnitRecord { Id = Guid.Parse("71000000-0000-0000-0000-000000000003"), Code = "kWh", Symbol = "kWh", Dimension = "energy", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "kWh", CatalogueVersion = "units-p0-v1", AliasesCsv = "kilowatt-hour" },
                new UnitRecord { Id = Guid.Parse("71000000-0000-0000-0000-000000000004"), Code = "tonne-km", Symbol = "t·km", Dimension = "transport-work", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "tonne-km", CatalogueVersion = "units-p0-v1", AliasesCsv = "t-km,tkm", CompositeExpression = "tonne*km" },
                new UnitRecord { Id = Guid.Parse("72000000-0000-0000-0000-000000000001"), Code = "kg", Symbol = "kg", Dimension = "mass", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "kg", CatalogueVersion = "units-p0-v2", AliasesCsv = "kilogram,kilograms" },
                new UnitRecord { Id = Guid.Parse("72000000-0000-0000-0000-000000000002"), Code = "g", Symbol = "g", Dimension = "mass", ScaleToCanonical = 0.001m, OffsetToCanonical = 0m, CanonicalCode = "kg", CatalogueVersion = "units-p0-v2", AliasesCsv = "gram,grams" },
                new UnitRecord { Id = Guid.Parse("72000000-0000-0000-0000-000000000003"), Code = "tonne", Symbol = "t", Dimension = "mass", ScaleToCanonical = 1000m, OffsetToCanonical = 0m, CanonicalCode = "kg", CatalogueVersion = "units-p0-v2", AliasesCsv = "ton,tons,tonnes" },
                new UnitRecord { Id = Guid.Parse("72000000-0000-0000-0000-000000000004"), Code = "kWh", Symbol = "kWh", Dimension = "energy", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "kWh", CatalogueVersion = "units-p0-v2", AliasesCsv = "kilowatt-hour" },
                new UnitRecord { Id = Guid.Parse("72000000-0000-0000-0000-000000000005"), Code = "tonne-km", Symbol = "t·km", Dimension = "transport-work", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "tonne-km", CatalogueVersion = "units-p0-v2", AliasesCsv = "t-km,tkm", CompositeExpression = "tonne*km" },
                new UnitRecord { Id = Guid.Parse("72000000-0000-0000-0000-000000000006"), Code = "piece", Symbol = "pc", Dimension = "count", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "piece", CatalogueVersion = "units-p0-v2", AliasesCsv = "pieces,item,items,件,個" });
        });
        builder.Entity<EmissionFactorVersionRecord>(entity =>
        {
            entity.ToTable("emission_factor_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Value).HasPrecision(30, 15);
            entity.Property(item => item.Name).HasMaxLength(500);
            entity.Property(item => item.NumeratorUnitCode).HasMaxLength(50);
            entity.Property(item => item.DenominatorUnitCode).HasMaxLength(50);
            entity.Property(item => item.PublicationStatus).HasMaxLength(30);
            entity.Property(item => item.SourceType).HasMaxLength(50);
            entity.Property(item => item.SourceName).HasMaxLength(300);
            entity.Property(item => item.SourceReference).HasMaxLength(500);
            entity.Property(item => item.DatasetName).HasMaxLength(300);
            entity.Property(item => item.OriginalDocumentName).HasMaxLength(300);
            entity.Property(item => item.OriginalDocumentSha256).HasMaxLength(64);
            entity.Property(item => item.Applicability).HasMaxLength(2000);
            entity.Property(item => item.ReviewStatus).HasMaxLength(30);
            entity.HasIndex(item => new { item.FactorId, item.VersionNumber }).IsUnique();
            entity.HasOne<EmissionFactorVersionRecord>().WithMany().HasForeignKey(item => item.SupersedesVersionId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureCalculations(ModelBuilder builder)
    {
        builder.Entity<CalculationRunRecord>(entity =>
        {
            entity.ToTable("calculation_runs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CanonicalInputManifest).HasColumnType("jsonb");
            entity.Property(item => item.DataQualitySummaryJson).HasColumnType("jsonb");
            entity.Property(item => item.InputSha256).HasMaxLength(64);
            entity.Property(item => item.ProductTotal).HasPrecision(38, 15);
            entity.HasIndex(item => new { item.OrganizationId, item.InputSha256 });
            entity.HasOne<InventoryProjectVersionRecord>().WithMany().HasForeignKey(item => item.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CalculationRunRecord>().WithMany().HasForeignKey(item => item.SupersedesRunId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CalculationLineRecord>(entity =>
        {
            entity.ToTable("calculation_line_items");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CanonicalActivityValue).HasPrecision(30, 12);
            entity.Property(item => item.FactorValue).HasPrecision(30, 15);
            entity.Property(item => item.AllocationFactor).HasPrecision(18, 15);
            entity.Property(item => item.Emissions).HasPrecision(38, 15);
            entity.Property(item => item.ActivityAmountFormulaId).HasMaxLength(150);
            entity.Property(item => item.FormulaInputsJson).HasColumnType("jsonb");
            entity.Property(item => item.FormulaTraceJson).HasColumnType("jsonb");
            entity.Property(item => item.GovernanceTraceJson).HasColumnType("jsonb");
            entity.HasOne<CalculationRunRecord>().WithMany().HasForeignKey(item => item.CalculationRunId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CalculationStageSummaryRecord>(entity =>
        {
            entity.ToTable("calculation_stage_summaries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Emissions).HasPrecision(38, 15);
            entity.HasIndex(item => new { item.CalculationRunId, item.LifecycleStage }).IsUnique();
            entity.HasOne<CalculationRunRecord>().WithMany().HasForeignKey(item => item.CalculationRunId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CalculationWarningRecord>(entity =>
        {
            entity.ToTable("calculation_warnings");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Code).HasMaxLength(100);
            entity.Property(item => item.Message).HasMaxLength(1000);
            entity.HasOne<CalculationRunRecord>().WithMany().HasForeignKey(item => item.CalculationRunId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAudit(ModelBuilder builder)
    {
        builder.Entity<AuditEventRecord>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.MetadataJson).HasColumnType("jsonb");
            entity.Property(item => item.CorrelationId).HasMaxLength(100);
            entity.HasIndex(item => new { item.OrganizationId, item.Timestamp });
        });
    }

    private static void ConfigureGovernance(ModelBuilder builder)
    {
        builder.Entity<GovernanceDefinitionRecord>(entity =>
        {
            entity.ToTable("governance_definition_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.DefinitionType).HasMaxLength(100);
            entity.Property(item => item.StableKey).HasMaxLength(300);
            entity.Property(item => item.Name).HasMaxLength(500);
            entity.Property(item => item.PublicationStatus).HasMaxLength(30);
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb");
            entity.Property(item => item.CanonicalSha256).HasMaxLength(64);
            entity.Property(item => item.SourceStableId).HasMaxLength(300);
            entity.Property(item => item.SourceName).HasMaxLength(300);
            entity.Property(item => item.SourceUrl).HasMaxLength(1000);
            entity.Property(item => item.SourceDatasetVersion).HasMaxLength(200);
            entity.Property(item => item.LicenseCode).HasMaxLength(100);
            entity.HasIndex(item => new { item.DefinitionType, item.StableKey, item.VersionNumber, item.OrganizationId }).IsUnique();
            entity.HasIndex(item => new { item.DefinitionType, item.SourceStableId, item.VersionNumber });
            entity.HasOne<GovernanceDefinitionRecord>().WithMany().HasForeignKey(item => item.SupersedesVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EvidenceDocumentVersionRecord>().WithMany().HasForeignKey(item => item.SourceEvidenceDocumentVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<OrganizationDefinitionActivationRecord>(entity =>
        {
            entity.ToTable("organization_definition_activations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.DisplayAlias).HasMaxLength(500);
            entity.Property(item => item.InternalCategory).HasMaxLength(200);
            entity.Property(item => item.ApplicabilityNote).HasMaxLength(2000);
            entity.Property(item => item.OverridePayloadJson).HasColumnType("jsonb");
            entity.HasIndex(item => new { item.OrganizationId, item.DefinitionVersionId }).IsUnique();
            entity.HasOne<GovernanceDefinitionRecord>().WithMany().HasForeignKey(item => item.DefinitionVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ProjectGovernanceRecord>(entity =>
        {
            entity.ToTable("project_governance_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.RecordType).HasMaxLength(100);
            entity.Property(item => item.StableKey).HasMaxLength(300);
            entity.Property(item => item.Status).HasMaxLength(50);
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb");
            entity.Property(item => item.CanonicalSha256).HasMaxLength(64);
            entity.Property(item => item.LockReason).HasMaxLength(500);
            entity.HasIndex(item => new { item.ProjectVersionId, item.RecordType, item.StableKey, item.VersionNumber }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.ProjectVersionId, item.RecordType, item.Status });
            entity.HasOne<InventoryProjectVersionRecord>().WithMany().HasForeignKey(item => item.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<GovernanceEventRecord>(entity =>
        {
            entity.ToTable("governance_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EventType).HasMaxLength(150);
            entity.Property(item => item.EntityType).HasMaxLength(100);
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb");
            entity.Property(item => item.PayloadSha256).HasMaxLength(64);
            entity.Property(item => item.CorrelationId).HasMaxLength(100);
            entity.HasIndex(item => new { item.OrganizationId, item.ProjectVersionId, item.OccurredAt });
            entity.HasOne<InventoryProjectVersionRecord>().WithMany().HasForeignKey(item => item.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EvidenceDocumentRecord>(entity =>
        {
            entity.ToTable("evidence_documents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).HasMaxLength(500);
            entity.Property(item => item.Category).HasMaxLength(100);
            entity.HasIndex(item => new { item.OrganizationId, item.CreatedAt });
        });
        builder.Entity<EvidenceDocumentVersionRecord>(entity =>
        {
            entity.ToTable("evidence_document_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.OriginalFileName).HasMaxLength(500);
            entity.Property(item => item.ContentType).HasMaxLength(200);
            entity.Property(item => item.ObjectKey).HasMaxLength(1000);
            entity.Property(item => item.ObjectStorageVersion).HasMaxLength(300);
            entity.Property(item => item.Sha256).HasMaxLength(64);
            entity.Property(item => item.ScanStatus).HasMaxLength(30);
            entity.Property(item => item.ScanEngine).HasMaxLength(100);
            entity.Property(item => item.ScanEngineVersion).HasMaxLength(100);
            entity.Property(item => item.ScanSignatureVersion).HasMaxLength(100);
            entity.Property(item => item.ScanDetails).HasMaxLength(2000);
            entity.Property(item => item.StorageStatus).HasMaxLength(50);
            entity.HasIndex(item => new { item.DocumentId, item.VersionNumber }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.Sha256, item.SizeBytes });
            entity.HasOne<EvidenceDocumentRecord>().WithMany().HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EvidenceDocumentVersionRecord>().WithMany().HasForeignKey(item => item.ReplacesVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EvidenceLinkRecord>(entity =>
        {
            entity.ToTable("evidence_links");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.TargetType).HasMaxLength(100);
            entity.Property(item => item.Purpose).HasMaxLength(1000);
            entity.HasIndex(item => new { item.OrganizationId, item.DocumentVersionId, item.TargetType, item.TargetId }).IsUnique();
            entity.HasOne<EvidenceDocumentVersionRecord>().WithMany().HasForeignKey(item => item.DocumentVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EvidenceAccessLogRecord>(entity =>
        {
            entity.ToTable("evidence_access_logs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Action).HasMaxLength(50);
            entity.Property(item => item.IpAddressHash).HasMaxLength(64);
            entity.Property(item => item.Reason).HasMaxLength(1000);
            entity.HasIndex(item => new { item.OrganizationId, item.DocumentVersionId, item.OccurredAt });
            entity.HasOne<EvidenceDocumentVersionRecord>().WithMany().HasForeignKey(item => item.DocumentVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EvidenceRetentionLockRecord>(entity =>
        {
            entity.ToTable("evidence_retention_locks");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Trigger).HasMaxLength(100);
            entity.HasIndex(item => new { item.DocumentVersionId, item.LockedAt });
            entity.HasOne<EvidenceDocumentVersionRecord>().WithMany().HasForeignKey(item => item.DocumentVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GovernanceDefinitionRecord>().WithMany().HasForeignKey(item => item.PolicyDefinitionVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<VerificationArchiveRecord>(entity =>
        {
            entity.ToTable("verification_archives");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ExportSchemaVersion).HasMaxLength(100);
            entity.Property(item => item.ArchiveSha256).HasMaxLength(64);
            entity.Property(item => item.FileIndexJson).HasColumnType("jsonb");
            entity.HasIndex(item => new { item.ProjectVersionId, item.CalculationRunId, item.ArchiveSha256 }).IsUnique();
            entity.HasOne<InventoryProjectVersionRecord>().WithMany().HasForeignKey(item => item.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CalculationRunRecord>().WithMany().HasForeignKey(item => item.CalculationRunId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ProjectImpactRecord>(entity =>
        {
            entity.ToTable("project_impacts");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ChangeType).HasMaxLength(100);
            entity.Property(item => item.DependencyType).HasMaxLength(100);
            entity.Property(item => item.DependencyKey).HasMaxLength(300);
            entity.Property(item => item.PreviousVersion).HasMaxLength(200);
            entity.Property(item => item.CurrentVersion).HasMaxLength(200);
            entity.Property(item => item.AffectedEmissions).HasPrecision(38, 15);
            entity.Property(item => item.LifecycleStage).HasMaxLength(100);
            entity.Property(item => item.Reason).HasMaxLength(2000);
            entity.HasIndex(item => new { item.OrganizationId, item.ProjectVersionId, item.DetectedAt });
            entity.HasOne<InventoryProjectVersionRecord>().WithMany().HasForeignKey(item => item.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureLegacyStaging(ModelBuilder builder)
    {
        builder.Entity<LegacyImportBatchRecord>(entity =>
        {
            entity.ToTable("import_batches", "staging");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.SourceFileName).HasMaxLength(300);
            entity.Property(item => item.SourceFileSha256).HasMaxLength(64);
            entity.Property(item => item.EntityType).HasMaxLength(100);
            entity.Property(item => item.Status).HasMaxLength(30);
            entity.HasIndex(item => new { item.OrganizationId, item.SourceFileSha256, item.EntityType }).IsUnique();
        });
        builder.Entity<LegacyStagingRowRecord>(entity =>
        {
            entity.ToTable("rows", "staging");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.RawSha256).HasMaxLength(64);
            entity.Property(item => item.ParseStatus).HasMaxLength(30);
            entity.HasIndex(item => new { item.ImportBatchId, item.SourceRowNumber }).IsUnique();
            entity.HasOne<LegacyImportBatchRecord>().WithMany().HasForeignKey(item => item.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<LegacyImportConflictRecord>(entity =>
        {
            entity.ToTable("conflicts", "staging");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ConflictKey).HasMaxLength(500);
            entity.HasIndex(item => new { item.ImportBatchId, item.ConflictKey });
            entity.HasOne<LegacyImportBatchRecord>().WithMany().HasForeignKey(item => item.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LegacyStagingRowRecord>().WithMany().HasForeignKey(item => item.StagingRowId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void ConfigureTenantFilters(ModelBuilder builder)
    {
        builder.Entity<GovernanceDefinitionRecord>().HasQueryFilter(item => item.OrganizationId == null || (_organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId));
        builder.Entity<OrganizationRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.Id == _organizationScope.OrganizationId);
        builder.Entity<OrganizationMailSettingsRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<OrganizationMembershipRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<OrganizationInvitationRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<FacilityRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ProductRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ProductVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<InventoryProjectVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<PcrVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<PcrStageRuleRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<LifecycleStageDeclarationRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EmissionFactorVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ActivityDataRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EvidenceFileRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationRunRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationLineRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationStageSummaryRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationWarningRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<AuditEventRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<OrganizationDefinitionActivationRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ProjectGovernanceRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<GovernanceEventRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EvidenceDocumentRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EvidenceDocumentVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EvidenceLinkRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EvidenceAccessLogRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EvidenceRetentionLockRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<VerificationArchiveRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ProjectImpactRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<LegacyImportBatchRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<LegacyStagingRowRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<LegacyImportConflictRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
    }

    private void ValidateChanges()
    {
        var immutableTypes = new[]
        {
            typeof(CalculationRunRecord), typeof(CalculationLineRecord),
            typeof(CalculationStageSummaryRecord), typeof(CalculationWarningRecord), typeof(AuditEventRecord),
            typeof(GovernanceEventRecord), typeof(EvidenceAccessLogRecord), typeof(EvidenceRetentionLockRecord),
            typeof(VerificationArchiveRecord), typeof(ProjectImpactRecord),
            typeof(LegacyImportBatchRecord), typeof(LegacyStagingRowRecord), typeof(LegacyImportConflictRecord)
        };
        var immutableChange = ChangeTracker.Entries().FirstOrDefault(entry =>
            immutableTypes.Contains(entry.Metadata.ClrType)
            && entry.State is EntityState.Modified or EntityState.Deleted);
        if (immutableChange is not null)
        {
            throw new InvalidOperationException($"{immutableChange.Metadata.ClrType.Name} 是不可變 append-only 資料。");
        }

        var publishedPcrChange = ChangeTracker.Entries<PcrVersionRecord>()
            .FirstOrDefault(entry =>
                entry.State == EntityState.Modified
                && !string.Equals(
                    entry.Property(item => item.PublicationStatus).OriginalValue,
                    "Draft",
                    StringComparison.Ordinal)
                && entry.Properties.Any(property =>
                    property.IsModified
                    && property.Metadata.Name is not nameof(PcrVersionRecord.PublicationStatus)
                        and not nameof(PcrVersionRecord.WithdrawnAt)
                        and not nameof(PcrVersionRecord.DeprecatedAt)
                        and not nameof(PcrVersionRecord.DeprecationReason)));
        if (publishedPcrChange is not null)
        {
            throw new InvalidOperationException("已發布或撤回的 PCR 規則版本不可修改；請建立新版本。");
        }

        var changedStageRules = ChangeTracker.Entries<PcrStageRuleRecord>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .ToArray();
        if (changedStageRules.Length > 0)
        {
            var changedPcrIds = changedStageRules
                .Select(entry => entry.Entity.PcrVersionId)
                .Distinct()
                .ToArray();
            var hasPublishedStageRuleChange = PcrVersions
                .AsNoTracking()
                .Any(item => changedPcrIds.Contains(item.Id)
                    && item.PublicationStatus != "Draft");
            if (hasPublishedStageRuleChange)
            {
                throw new InvalidOperationException("已發布或撤回的 PCR 階段規則不可修改；請建立新版本。");
            }
        }

        var publishedDefinitionChange = ChangeTracker.Entries<GovernanceDefinitionRecord>()
            .FirstOrDefault(entry =>
                (entry.State is EntityState.Modified or EntityState.Deleted)
                && !string.Equals(entry.Property(item => item.PublicationStatus).OriginalValue, "Draft", StringComparison.Ordinal)
                && (entry.State == EntityState.Deleted
                    || entry.Properties.Any(property =>
                        property.IsModified
                        && property.Metadata.Name is not nameof(GovernanceDefinitionRecord.PublicationStatus)
                            and not nameof(GovernanceDefinitionRecord.WithdrawnAt))));
        if (publishedDefinitionChange is not null)
        {
            throw new InvalidOperationException("已發布、撤回或取代的治理定義版本不可修改；請建立新版本。");
        }

        var immutableGovernanceChange = ChangeTracker.Entries<ProjectGovernanceRecord>()
            .FirstOrDefault(entry => entry.Entity.IsImmutable && (entry.State is EntityState.Modified or EntityState.Deleted));
        if (immutableGovernanceChange is not null)
        {
            throw new InvalidOperationException("已鎖定的專案治理版本不可修改；請建立新版本。");
        }

        var evidenceVersionChange = ChangeTracker.Entries<EvidenceDocumentVersionRecord>()
            .FirstOrDefault(entry =>
                entry.State == EntityState.Deleted
                || (entry.State == EntityState.Modified
                    && entry.Properties.Any(property =>
                        property.IsModified
                        && property.Metadata.Name is not nameof(EvidenceDocumentVersionRecord.StorageStatus))));
        if (evidenceVersionChange is not null)
        {
            throw new InvalidOperationException("佐證文件版本不可覆寫或刪除；替換時必須建立新版本。");
        }

        foreach (var entry in ChangeTracker.Entries<IOrganizationOwned>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (!_organizationScope.OrganizationId.HasValue || entry.Entity.OrganizationId != _organizationScope.OrganizationId.Value)
            {
                throw new InvalidOperationException("資料寫入不符合目前組織範圍。");
            }
        }
    }
}
