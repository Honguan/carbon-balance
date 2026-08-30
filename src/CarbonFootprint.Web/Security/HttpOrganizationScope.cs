using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Infrastructure.Identity;

namespace CarbonFootprint.Web.Security;

public sealed class HttpOrganizationScope : IOrganizationScope
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpOrganizationScope(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? OrganizationId
    {
        get
        {
            var claims = _httpContextAccessor.HttpContext?.User
                .FindAll(OrganizationClaimsPrincipalFactory.OrganizationClaimType)
                .ToArray() ?? [];
            return claims.Length == 1
                && Guid.TryParse(claims[0].Value, out var organizationId)
                && organizationId != Guid.Empty
                ? organizationId
                : null;
        }
    }
}
