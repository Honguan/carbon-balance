using Microsoft.AspNetCore.Authorization;

namespace CarbonFootprint.Web.Security;

// Retained as a compatibility requirement for existing authorization call sites.
// The simplified system treats a signed-in, role-authorized session as sufficient
// and no longer requires a second authentication factor.
public sealed class MfaEnabledRequirement : IAuthorizationRequirement;

public sealed class MfaEnabledHandler : AuthorizationHandler<MfaEnabledRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MfaEnabledRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
