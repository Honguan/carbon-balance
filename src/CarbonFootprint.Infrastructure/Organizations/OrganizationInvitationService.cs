using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Organizations;

public sealed class OrganizationInvitationService
{
    private readonly DbContextOptions<CarbonFootprintDbContext> _options;

    public OrganizationInvitationService(DbContextOptions<CarbonFootprintDbContext> options)
    {
        _options = options;
    }

    public async Task<string> CreateAsync(
        Guid organizationId,
        Guid invitedBy,
        string email,
        OrganizationRole role,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        {
            throw new ArgumentException("請輸入有效的邀請 Email。", nameof(email));
        }
        if (role is OrganizationRole.Owner)
        {
            throw new ArgumentException("邀請不可直接授與擁有者角色。", nameof(role));
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var context = CreateContext(organizationId);
        var duplicate = await context.OrganizationInvitations.AnyAsync(
            item => item.Email == normalizedEmail && item.AcceptedAt == null && item.RevokedAt == null && item.ExpiresAt > DateTimeOffset.UtcNow,
            cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException("此 Email 已有有效邀請。");
        }

        var invitationId = Guid.NewGuid();
        context.OrganizationInvitations.Add(new OrganizationInvitationRecord
        {
            Id = invitationId,
            OrganizationId = organizationId,
            Email = normalizedEmail,
            Role = role.ToString(),
            TokenSha256 = Sha256(token),
            InvitedBy = invitedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        context.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = invitedBy,
            OrganizationId = organizationId,
            Action = "organization.invitation.created",
            ResourceType = "OrganizationInvitation",
            ResourceId = invitationId,
            CorrelationId = invitationId.ToString("N"),
            MetadataJson = "{}"
        });
        await context.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<Guid> AcceptAsync(ApplicationUser user, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("邀請 token 不可空白。", nameof(token));
        }

        var tokenHash = Sha256(token);
        Guid organizationId;
        await using (var lookupContext = new CarbonFootprintDbContext(_options, new UnscopedOrganizationScope()))
        {
            organizationId = await lookupContext.OrganizationInvitations
                .IgnoreQueryFilters()
                .Where(item => item.TokenSha256 == tokenHash)
                .Select(item => item.OrganizationId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        if (organizationId == Guid.Empty)
        {
            throw new InvalidOperationException("邀請不存在或已失效。");
        }

        await using var context = CreateContext(organizationId);
        var invitation = await context.OrganizationInvitations.SingleAsync(item => item.TokenSha256 == tokenHash, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (invitation.AcceptedAt.HasValue || invitation.RevokedAt.HasValue || invitation.ExpiresAt <= now)
        {
            throw new InvalidOperationException("邀請不存在或已失效。");
        }
        if (!string.Equals(invitation.Email, user.NormalizedEmail, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("登入帳號與受邀 Email 不一致。");
        }
        if (await context.UserClaims.AnyAsync(item => item.UserId == user.Id && item.ClaimType == "organization_id", cancellationToken))
        {
            throw new InvalidOperationException("此帳號已加入其他組織。");
        }

        invitation.AcceptedAt = now;
        context.OrganizationMemberships.Add(new OrganizationMembershipRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = user.Id,
            Role = invitation.Role,
            CreatedAt = now
        });
        context.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            UserId = user.Id,
            ClaimType = "organization_id",
            ClaimValue = organizationId.ToString()
        });
        context.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = now,
            ActorId = user.Id,
            OrganizationId = organizationId,
            Action = "organization.invitation.accepted",
            ResourceType = "OrganizationInvitation",
            ResourceId = invitation.Id,
            CorrelationId = invitation.Id.ToString("N"),
            MetadataJson = "{}"
        });
        await context.SaveChangesAsync(cancellationToken);
        return organizationId;
    }

    private CarbonFootprintDbContext CreateContext(Guid organizationId) =>
        new(_options, new ExplicitOrganizationScope(organizationId));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ExplicitOrganizationScope(Guid Value) : IOrganizationScope
    {
        public Guid? OrganizationId => Value;
    }
}
