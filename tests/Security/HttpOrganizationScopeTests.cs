using System.Security.Claims;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Http;

namespace CarbonFootprint.Security.Tests;

public sealed class HttpOrganizationScopeTests
{
    [Fact]
    public void OrganizationId_RequiresExactlyOneValidOrganizationClaim()
    {
        var organizationId = Guid.NewGuid();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var scope = new HttpOrganizationScope(accessor);

        accessor.HttpContext.User = CreatePrincipal(organizationId.ToString());
        Assert.Equal(organizationId, scope.OrganizationId);

        accessor.HttpContext.User = CreatePrincipal(organizationId.ToString(), Guid.NewGuid().ToString());
        Assert.Null(scope.OrganizationId);

        accessor.HttpContext.User = CreatePrincipal("invalid");
        Assert.Null(scope.OrganizationId);

        accessor.HttpContext.User = CreatePrincipal(Guid.Empty.ToString());
        Assert.Null(scope.OrganizationId);
    }

    private static ClaimsPrincipal CreatePrincipal(params string[] organizationIds) =>
        new(new ClaimsIdentity(
            organizationIds.Select(value =>
                new Claim(OrganizationClaimsPrincipalFactory.OrganizationClaimType, value)),
            "test"));
}
