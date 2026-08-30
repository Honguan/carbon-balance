using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace CarbonFootprint.Web.Security;

public static class MfaAuthentication
{
    public const string AuthenticatedAtProperty = "authenticated_at";
    public const string VerifiedAtProperty = "mfa_verified_at";
    public static readonly TimeSpan DefaultFreshness = TimeSpan.FromMinutes(15);

    public static bool IsFresh(
        AuthenticateResult authentication,
        ClaimsPrincipal currentUser,
        DateTimeOffset now,
        TimeSpan freshness,
        bool requireMfa)
    {
        var principal = authentication.Principal;
        var propertyName = requireMfa ? VerifiedAtProperty : AuthenticatedAtProperty;
        if (!authentication.Succeeded
            || principal is null
            || (requireMfa
                ? !principal.HasClaim("amr", "mfa")
                : !principal.HasClaim(claim => claim.Type == "amr" && claim.Value is "pwd" or "mfa"))
            || principal.FindFirstValue(ClaimTypes.NameIdentifier)
                != currentUser.FindFirstValue(ClaimTypes.NameIdentifier)
            || authentication.Properties is null
            || !authentication.Properties.Items.TryGetValue(propertyName, out var authenticatedAtValue)
            || !long.TryParse(
                authenticatedAtValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var authenticatedAtSeconds))
        {
            return false;
        }

        DateTimeOffset authenticatedAt;
        try
        {
            authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(authenticatedAtSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var age = now - authenticatedAt;
        return age >= TimeSpan.Zero && age <= freshness;
    }
}

public sealed record MfaAuthenticationSettings(TimeSpan Freshness);

public sealed class MfaEnabledRequirement : IAuthorizationRequirement;

public sealed class MfaEnabledHandler : AuthorizationHandler<MfaEnabledRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly MfaAuthenticationSettings _settings;

    public MfaEnabledHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        MfaAuthenticationSettings settings)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _settings = settings;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MfaEnabledRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || _httpContextAccessor.HttpContext is not { } httpContext)
        {
            return;
        }

        var authentication = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (MfaAuthentication.IsFresh(
            authentication,
            context.User,
            _timeProvider.GetUtcNow(),
            _settings.Freshness,
            requireMfa: true))
        {
            context.Succeed(requirement);
        }
    }
}
