using System.Security.Claims;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Security;

public sealed record GovernanceActorContext(
    Guid UserId,
    OrganizationRole Role,
    IReadOnlySet<string> WorkflowRoles,
    bool HasVerifiedMfa);

public sealed class GovernanceAccessService
{
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IOrganizationScope _organizationScope;
    private readonly UserManager<ApplicationUser> _userManager;

    public GovernanceAccessService(
        CarbonFootprintDbContext dbContext,
        IOrganizationScope organizationScope,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _organizationScope = organizationScope;
        _userManager = userManager;
    }

    public async Task<GovernanceActorContext?> ResolveAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!_organizationScope.OrganizationId.HasValue
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return null;
        }

        var roleName = await _dbContext.OrganizationMemberships.AsNoTracking()
            .Where(item => item.UserId == userId && item.RevokedAt == null)
            .Select(item => item.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (!Enum.TryParse<OrganizationRole>(roleName, out var role))
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        var hasVerifiedMfa = user?.TwoFactorEnabled == true && HasMfaClaim(principal);
        return new(
            userId,
            role,
            WorkflowRoles(role),
            hasVerifiedMfa);
    }

    public static bool HasMfaClaim(ClaimsPrincipal principal) =>
        principal.Claims.Any(claim =>
            (string.Equals(claim.Type, "amr", StringComparison.OrdinalIgnoreCase)
                || string.Equals(claim.Type, ClaimTypes.AuthenticationMethod, StringComparison.OrdinalIgnoreCase))
            && (string.Equals(claim.Value, "mfa", StringComparison.OrdinalIgnoreCase)
                || string.Equals(claim.Value, "otp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(claim.Value, "totp", StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlySet<string> WorkflowRoles(OrganizationRole role)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        switch (role)
        {
            case OrganizationRole.Owner:
            case OrganizationRole.Administrator:
                roles.Add("Administrator");
                roles.Add("Reviewer");
                roles.Add("Verifier");
                break;
            case OrganizationRole.Reviewer:
                roles.Add("Reviewer");
                break;
            case OrganizationRole.Verifier:
                roles.Add("Verifier");
                break;
            case OrganizationRole.Contributor:
                roles.Add("Contributor");
                break;
            case OrganizationRole.Viewer:
                roles.Add("Viewer");
                break;
        }

        return roles;
    }
}
