using System.Security.Claims;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Security;

public sealed record OrganizationPermissionRequirement(OrganizationPermission Permission) : IAuthorizationRequirement;

public sealed class OrganizationPermissionHandler : AuthorizationHandler<OrganizationPermissionRequirement>
{
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IOrganizationScope _organizationScope;

    public OrganizationPermissionHandler(
        CarbonFootprintDbContext dbContext,
        IOrganizationScope organizationScope)
    {
        _dbContext = dbContext;
        _organizationScope = organizationScope;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrganizationPermissionRequirement requirement)
    {
        if (!_organizationScope.OrganizationId.HasValue ||
            !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        var roleName = await _dbContext.OrganizationMemberships
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.RevokedAt == null)
            .Select(item => item.Role)
            .SingleOrDefaultAsync();
        if (Enum.TryParse<OrganizationRole>(roleName, out var role) &&
            OrganizationPermissions.IsAllowed(role, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
