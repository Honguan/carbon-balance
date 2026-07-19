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
    public DbSet<OrganizationMembershipRecord> OrganizationMemberships => Set<OrganizationMembershipRecord>();
    public DbSet<OrganizationInvitationRecord> OrganizationInvitations => Set<OrganizationInvitationRecord>();
    public DbSet<FacilityRecord> Facilities => Set<FacilityRecord>();
    public DbSet<ProductRecord> Products => Set<ProductRecord>();
    public DbSet<ProductVersionRecord> ProductVersions => Set<ProductVersionRecord>();
    public DbSet<InventoryProjectVersionRecord> InventoryProjectVersions => Set<InventoryProjectVersionRecord>();
    public DbSet<PcrVersionRecord> PcrVersions => Set<PcrVersionRecord>();
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
            entity.Property(item => item.OriginalDocumentSha256).HasMaxLength(64);
            entity.Property(item => item.ReviewStatus).HasMaxLength(30);
            entity.HasIndex(item => new { item.OrganizationId, item.RegistrationNumber, item.VersionNumber }).IsUnique();
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
            entity.Property(item => item.EvidenceSha256).HasMaxLength(64);
            entity.Property(item => item.AllocationFactor).HasPrecision(18, 15);
            entity.Property(item => item.EstimationReason).HasMaxLength(4000);
            entity.Property(item => item.DataQuality).HasMaxLength(100);
            entity.HasOne<InventoryProjectVersionRecord>().WithMany().HasForeignKey(item => item.InventoryProjectVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EmissionFactorVersionRecord>().WithMany().HasForeignKey(item => item.FactorVersionId).OnDelete(DeleteBehavior.Restrict);
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
                new UnitRecord { Id = Guid.Parse("72000000-0000-0000-0000-000000000005"), Code = "tonne-km", Symbol = "t·km", Dimension = "transport-work", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "tonne-km", CatalogueVersion = "units-p0-v2", AliasesCsv = "t-km,tkm", CompositeExpression = "tonne*km" });
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
        builder.Entity<OrganizationRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.Id == _organizationScope.OrganizationId);
        builder.Entity<OrganizationMembershipRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<OrganizationInvitationRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<FacilityRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ProductRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ProductVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<InventoryProjectVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<PcrVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<LifecycleStageDeclarationRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EmissionFactorVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ActivityDataRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EvidenceFileRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationRunRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationLineRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationStageSummaryRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationWarningRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<AuditEventRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
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
            typeof(LegacyImportBatchRecord), typeof(LegacyStagingRowRecord), typeof(LegacyImportConflictRecord)
        };
        var immutableChange = ChangeTracker.Entries().FirstOrDefault(entry =>
            immutableTypes.Contains(entry.Metadata.ClrType)
            && entry.State is EntityState.Modified or EntityState.Deleted);
        if (immutableChange is not null)
        {
            throw new InvalidOperationException($"{immutableChange.Metadata.ClrType.Name} 是不可變 append-only 資料。");
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
