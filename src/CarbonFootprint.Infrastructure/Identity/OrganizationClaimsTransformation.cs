using System.Security.Claims;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Identity;

public sealed class OrganizationClaimsTransformation(CarbonFootprintDbContext dbContext) : IClaimsTransformation
{
    public const string OrganizationClaimType = "organization_id";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        foreach (var claimsIdentity in principal.Identities)
        {
            foreach (var claim in claimsIdentity.FindAll(OrganizationClaimType).ToArray())
            {
                claimsIdentity.RemoveClaim(claim);
            }
        }

        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || userId == Guid.Empty)
        {
            return principal;
        }

        var organizationId = await dbContext.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(item => item.UserId == userId && item.RevokedAt == null)
            .Select(item => item.OrganizationId)
            .SingleOrDefaultAsync();
        if (organizationId != Guid.Empty && principal.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim(OrganizationClaimType, organizationId.ToString()));
        }

        return principal;
    }
}
