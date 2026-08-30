using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Domain.Modules.Organizations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CarbonFootprint.Infrastructure.Organizations;

public sealed class OrganizationOnboardingService
{
    private readonly DbContextOptions<CarbonFootprintDbContext> _options;
    public OrganizationOnboardingService(DbContextOptions<CarbonFootprintDbContext> options)
    {
        _options = options;
    }

    public async Task<Guid> CreateAsync(ApplicationUser user, string organizationName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organizationName))
        {
            throw new ArgumentException("組織名稱不可為空。", nameof(organizationName));
        }

        var organizationId = Guid.NewGuid();
        await using (var dbContext = new CarbonFootprintDbContext(_options, new ExplicitOrganizationScope(organizationId)))
        {
            if (await dbContext.OrganizationMemberships.IgnoreQueryFilters().AnyAsync(
                    membership => membership.UserId == user.Id && membership.RevokedAt == null,
                    cancellationToken))
            {
                throw new InvalidOperationException("此帳號已有目前組織；P0 onboarding 不允許重複建立。");
            }

            dbContext.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = organizationName.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            });
            dbContext.OrganizationMemberships.Add(new OrganizationMembershipRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                UserId = user.Id,
                Role = OrganizationRole.Owner.ToString(),
                CreatedAt = DateTimeOffset.UtcNow
            });
            dbContext.AuditEvents.Add(new AuditEventRecord
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ActorId = user.Id,
                OrganizationId = organizationId,
                Action = "organization.created",
                ResourceType = "Organization",
                ResourceId = organizationId,
                BeforeHash = null,
                AfterHash = null,
                CorrelationId = organizationId.ToString("N"),
                MetadataJson = "{}"
            });
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsActiveMembershipConflict(exception))
            {
                throw new InvalidOperationException("此帳號已有目前組織；P0 onboarding 不允許重複建立。", exception);
            }
        }

        return organizationId;
    }

    private sealed record ExplicitOrganizationScope(Guid Value) : IOrganizationScope
    {
        public Guid? OrganizationId => Value;
    }

    private static bool IsActiveMembershipConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } postgresException
        && postgresException.ConstraintName is "ux_organization_memberships_active_user"
            or "ix_organization_memberships_organization_id_user_id";
}
