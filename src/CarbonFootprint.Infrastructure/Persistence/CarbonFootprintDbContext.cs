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
    public DbSet<ProductRecord> Products => Set<ProductRecord>();
    public DbSet<ProductVersionRecord> ProductVersions => Set<ProductVersionRecord>();
    public DbSet<InventoryProjectVersionRecord> InventoryProjectVersions => Set<InventoryProjectVersionRecord>();
    public DbSet<PcrVersionRecord> PcrVersions => Set<PcrVersionRecord>();
    public DbSet<UnitRecord> Units => Set<UnitRecord>();
    public DbSet<EmissionFactorVersionRecord> EmissionFactorVersions => Set<EmissionFactorVersionRecord>();
    public DbSet<ActivityDataRecord> ActivityData => Set<ActivityDataRecord>();
    public DbSet<EvidenceFileRecord> EvidenceFiles => Set<EvidenceFileRecord>();
    public DbSet<CalculationRunRecord> CalculationRuns => Set<CalculationRunRecord>();
    public DbSet<CalculationLineRecord> CalculationLineItems => Set<CalculationLineRecord>();
    public DbSet<CalculationStageSummaryRecord> CalculationStageSummaries => Set<CalculationStageSummaryRecord>();
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();

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
    }

    private static void ConfigureProducts(ModelBuilder builder)
    {
        builder.Entity<ProductRecord>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(300);
            entity.HasOne<OrganizationRecord>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
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
            entity.Property(item => item.PcrVersion).HasMaxLength(200);
            entity.Property(item => item.WorkflowStatus).HasMaxLength(50);
            entity.HasIndex(item => new { item.ProductVersionId, item.VersionNumber }).IsUnique();
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
            entity.HasIndex(item => new { item.OrganizationId, item.RegistrationNumber, item.VersionNumber }).IsUnique();
        });
        builder.Entity<ActivityDataRecord>(entity =>
        {
            entity.ToTable("activity_data_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(300);
            entity.Property(item => item.RawValue).HasPrecision(30, 12);
            entity.Property(item => item.CanonicalValue).HasPrecision(30, 12);
            entity.Property(item => item.RawUnitCode).HasMaxLength(50);
            entity.Property(item => item.CanonicalUnitCode).HasMaxLength(50);
            entity.Property(item => item.ConversionRuleVersion).HasMaxLength(100);
            entity.Property(item => item.EvidenceSha256).HasMaxLength(64);
            entity.HasOne<InventoryProjectVersionRecord>().WithMany().HasForeignKey(item => item.InventoryProjectVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EmissionFactorVersionRecord>().WithMany().HasForeignKey(item => item.FactorVersionId).OnDelete(DeleteBehavior.Restrict);
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
            entity.HasIndex(item => new { item.Code, item.CatalogueVersion }).IsUnique();
            entity.HasData(
                new UnitRecord { Id = Guid.Parse("71000000-0000-0000-0000-000000000001"), Code = "kg", Symbol = "kg", Dimension = "mass", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "kg", CatalogueVersion = "units-p0-v1" },
                new UnitRecord { Id = Guid.Parse("71000000-0000-0000-0000-000000000002"), Code = "g", Symbol = "g", Dimension = "mass", ScaleToCanonical = 0.001m, OffsetToCanonical = 0m, CanonicalCode = "kg", CatalogueVersion = "units-p0-v1" },
                new UnitRecord { Id = Guid.Parse("71000000-0000-0000-0000-000000000003"), Code = "kWh", Symbol = "kWh", Dimension = "energy", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "kWh", CatalogueVersion = "units-p0-v1" },
                new UnitRecord { Id = Guid.Parse("71000000-0000-0000-0000-000000000004"), Code = "tonne-km", Symbol = "t·km", Dimension = "transport-work", ScaleToCanonical = 1m, OffsetToCanonical = 0m, CanonicalCode = "tonne-km", CatalogueVersion = "units-p0-v1" });
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
            entity.Property(item => item.Emissions).HasPrecision(38, 15);
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

    private void ConfigureTenantFilters(ModelBuilder builder)
    {
        builder.Entity<OrganizationRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.Id == _organizationScope.OrganizationId);
        builder.Entity<OrganizationMembershipRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ProductRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ProductVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<InventoryProjectVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<PcrVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EmissionFactorVersionRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<ActivityDataRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<EvidenceFileRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationRunRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationLineRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<CalculationStageSummaryRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
        builder.Entity<AuditEventRecord>().HasQueryFilter(item => _organizationScope.OrganizationId != null && item.OrganizationId == _organizationScope.OrganizationId);
    }

    private void ValidateChanges()
    {
        var immutableTypes = new[]
        {
            typeof(CalculationRunRecord), typeof(CalculationLineRecord),
            typeof(CalculationStageSummaryRecord), typeof(AuditEventRecord)
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
