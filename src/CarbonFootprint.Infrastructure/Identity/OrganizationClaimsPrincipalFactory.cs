using System.Security.Claims;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Infrastructure.Identity;

public sealed class OrganizationClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    public const string OrganizationClaimType = "organization_id";

    private readonly CarbonFootprintDbContext _dbContext;

    public OrganizationClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> options,
        CarbonFootprintDbContext dbContext)
        : base(userManager, roleManager, options)
    {
        _dbContext = dbContext;
    }

    public override async Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
    {
        var principal = await base.CreateAsync(user);
        var identity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in identity.FindAll(OrganizationClaimType).ToArray())
        {
            identity.RemoveClaim(claim);
        }

        var organizationId = await _dbContext.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(item => item.UserId == user.Id && item.RevokedAt == null)
            .Select(item => item.OrganizationId)
            .SingleOrDefaultAsync();
        if (organizationId != Guid.Empty)
        {
            identity.AddClaim(new Claim(OrganizationClaimType, organizationId.ToString()));
        }

        return principal;
    }
}
