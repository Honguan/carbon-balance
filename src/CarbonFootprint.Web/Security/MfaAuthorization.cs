using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace CarbonFootprint.Web.Security;

public sealed class MfaEnabledRequirement : IAuthorizationRequirement;

public sealed class MfaEnabledHandler : AuthorizationHandler<MfaEnabledRequirement>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public MfaEnabledHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MfaEnabledRequirement requirement)
    {
        var user = await _userManager.GetUserAsync(context.User);
        if (user is not null && await _userManager.GetTwoFactorEnabledAsync(user))
        {
            context.Succeed(requirement);
        }
    }
}
