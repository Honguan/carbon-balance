using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace CarbonFootprint.Web.Security;

public sealed record AuthenticationSessionSettings(
    TimeSpan IdleTimeout,
    TimeSpan AbsoluteLifetime,
    TimeSpan SecurityStampValidationInterval)
{
    public static AuthenticationSessionSettings Create(IConfiguration configuration, bool isDevelopment)
    {
        var prefix = "Security:Authentication";
        var settings = new AuthenticationSessionSettings(
            configuration.GetValue<TimeSpan?>($"{prefix}:SessionIdleTimeout")
                ?? (isDevelopment ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8)),
            configuration.GetValue<TimeSpan?>($"{prefix}:SessionAbsoluteLifetime")
                ?? (isDevelopment ? TimeSpan.FromDays(30) : TimeSpan.FromHours(24)),
            configuration.GetValue<TimeSpan?>($"{prefix}:SecurityStampValidationInterval")
                ?? (isDevelopment ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(5)));

        if (settings.IdleTimeout <= TimeSpan.Zero
            || settings.AbsoluteLifetime < settings.IdleTimeout
            || settings.SecurityStampValidationInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Security:Authentication 的 session 與 security-stamp 時限設定無效。");
        }

        return settings;
    }
}

public static class AuthenticationSession
{
    public const string StartedAtProperty = "session_started_at";

    public static bool IsWithinAbsoluteLifetime(
        AuthenticationProperties properties,
        DateTimeOffset now,
        TimeSpan absoluteLifetime)
    {
        if (!properties.Items.TryGetValue(StartedAtProperty, out var startedAtValue)
            || !long.TryParse(startedAtValue, NumberStyles.None, CultureInfo.InvariantCulture, out var startedAtSeconds))
        {
            return false;
        }

        DateTimeOffset startedAt;
        try
        {
            startedAt = DateTimeOffset.FromUnixTimeSeconds(startedAtSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var age = now - startedAt;
        return age >= TimeSpan.Zero && age <= absoluteLifetime;
    }

    public static void PreserveAuthenticationMethod(ClaimsPrincipal? principal, string? authenticationMethod)
    {
        if (principal?.Identity is ClaimsIdentity identity
            && authenticationMethod is "pwd" or "mfa"
            && !principal.HasClaim("amr", authenticationMethod))
        {
            identity.AddClaim(new Claim("amr", authenticationMethod));
        }
    }
}

public sealed record AuthenticationRateLimitRule(string Name, int PermitLimit, TimeSpan Window);

public static class AuthenticationRateLimits
{
    private static readonly AuthenticationRateLimitRule Login = new("login", 10, TimeSpan.FromMinutes(5));
    private static readonly AuthenticationRateLimitRule Registration = new("registration", 5, TimeSpan.FromMinutes(15));
    private static readonly AuthenticationRateLimitRule EmailResend = new("email-resend", 5, TimeSpan.FromMinutes(15));
    private static readonly AuthenticationRateLimitRule Recovery = new("recovery", 5, TimeSpan.FromMinutes(15));
    private static readonly AuthenticationRateLimitRule Invitation = new("invitation", 10, TimeSpan.FromMinutes(15));
    private static readonly AuthenticationRateLimitRule Mfa = new("mfa", 10, TimeSpan.FromMinutes(5));
    private static readonly IReadOnlyDictionary<string, AuthenticationRateLimitRule> Rules =
        new Dictionary<string, AuthenticationRateLimitRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["/Identity/Account/Login"] = Login,
            ["/Identity/Account/Register"] = Registration,
            ["/Identity/Account/ResendEmailConfirmation"] = EmailResend,
            ["/Identity/Account/ForgotPassword"] = Recovery,
            ["/Identity/Account/ResetPassword"] = Recovery,
            ["/AcceptInvitation"] = Invitation,
            ["/Identity/Account/LoginWith2fa"] = Mfa,
            ["/Identity/Account/LoginWithRecoveryCode"] = Mfa,
            ["/Identity/Account/Manage/EnableAuthenticator"] = Mfa,
            ["/Identity/Account/Manage/ResetAuthenticator"] = Mfa,
            ["/Identity/Account/Manage/GenerateRecoveryCodes"] = Mfa,
            ["/Identity/Account/Manage/Disable2fa"] = Mfa,
            ["/Identity/Account/Manage/ChangePassword"] = Recovery,
            ["/Identity/Account/Manage/SetPassword"] = Recovery
        };

    public static AuthenticationRateLimitRule? FindRule(PathString path)
    {
        var value = path.Value?.TrimEnd('/');
        return value is not null && Rules.TryGetValue(value, out var rule) ? rule : null;
    }

    public static AuthenticationRateLimitRule GetRule(string path) =>
        Rules.TryGetValue(path, out var rule)
            ? rule
            : throw new ArgumentOutOfRangeException(nameof(path));
}

public static class TrustedProxyConfiguration
{
    public static IPAddress[] Parse(IConfiguration configuration) =>
        (configuration.GetSection("Security:TrustedProxies").Get<string[]>() ?? [])
        .Select(value => IPAddress.TryParse(value, out var address)
            ? address
            : throw new InvalidOperationException($"Security:TrustedProxies 包含無效 IP：{value}"))
        .Distinct()
        .ToArray();

    public static void Configure(ForwardedHeadersOptions options, IEnumerable<IPAddress> trustedProxies)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var address in trustedProxies)
        {
            options.KnownProxies.Add(address);
        }
    }
}
