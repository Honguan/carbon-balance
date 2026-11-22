using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Organizations;
using Microsoft.AspNetCore.Authorization;

namespace CarbonFootprint.Web.Security;

/// <summary>Maps the application-layer authorization question onto the framework's policy requirements.</summary>
public sealed class OperationAuthorizer(IAuthorizationService authorizationService) : IOperationAuthorizer
{
    public async Task<bool> IsAllowedAsync(OperationContext context, OrganizationPermission permission, bool requireMfa)
    {
        var permitted = await authorizationService.AuthorizeAsync(
            context.User, resource: null, new OrganizationPermissionRequirement(permission));
        if (!permitted.Succeeded) return false;
        if (!requireMfa) return true;
        return (await authorizationService.AuthorizeAsync(context.User, resource: null, new MfaEnabledRequirement())).Succeeded;
    }
}
