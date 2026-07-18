using CarbonFootprint.Infrastructure.Persistence;

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
            var value = _httpContextAccessor.HttpContext?.User.FindFirst("organization_id")?.Value;
            return Guid.TryParse(value, out var organizationId) ? organizationId : null;
        }
    }
}

