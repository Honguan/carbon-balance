using System.Net;
using System.Security.Claims;
using CarbonFootprint.Infrastructure;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Security.Tests;

public sealed class WebSecurityTests : IClassFixture<WebSecurityTests.Factory>
{
    private readonly HttpClient _client;

    public WebSecurityTests(Factory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task MfaRequirement_RequiresFreshVerifiedApplicationTicket()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

        Assert.False(await AuthorizeMfaAsync("pwd", now, now));
        Assert.False(await AuthorizeMfaAsync("mfa", verifiedAt: null, now));
        Assert.True(await AuthorizeMfaAsync("mfa", now.AddMinutes(-15), now));
        Assert.False(await AuthorizeMfaAsync("mfa", now.AddMinutes(-16), now));
        Assert.False(await AuthorizeMfaAsync("mfa", now.AddSeconds(1), now));
        Assert.True(IsFreshAuthentication("pwd", now.AddMinutes(-15), now));
        Assert.False(IsFreshAuthentication("pwd", now.AddMinutes(-16), now));
        Assert.False(IsFreshAuthentication("unknown", now, now));
    }

    [Fact]
    public async Task ProductionAuthenticationPolicy_IsPassphraseFriendlyAndRejectsCompromisedPasswords()
    {
        await using var production = CreateIdentityProvider(useDevelopmentAuthenticationPolicy: false);
        var options = production.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var validator = Assert.Single(
            production.GetServices<IPasswordValidator<ApplicationUser>>(),
            item => item is CompromisedPasswordValidator);

        Assert.Equal(12, options.Password.RequiredLength);
        Assert.False(options.Password.RequireDigit);
        Assert.False(options.Password.RequireLowercase);
        Assert.False(options.Password.RequireUppercase);
        Assert.False(options.Password.RequireNonAlphanumeric);
        Assert.False((await validator.ValidateAsync(
            null!,
            new ApplicationUser(),
            "password1234")).Succeeded);
        Assert.False((await validator.ValidateAsync(
            null!,
            new ApplicationUser(),
            "PasswordPassword1")).Succeeded);
        Assert.False((await validator.ValidateAsync(
            null!,
            new ApplicationUser(),
            "letmeinplease1")).Succeeded);
        Assert.False((await validator.ValidateAsync(
            null!,
            new ApplicationUser(),
            "administrator")).Succeeded);
        Assert.True((await validator.ValidateAsync(
            null!,
            new ApplicationUser(),
            "correct horse battery staple")).Succeeded);

        await using var development = CreateIdentityProvider(useDevelopmentAuthenticationPolicy: true);
        Assert.Equal(
            6,
            development.GetRequiredService<IOptions<IdentityOptions>>().Value.Password.RequiredLength);
    }

    [Fact]
    public void ProductionSessionPolicy_HasIdleAbsoluteAndStampValidationBounds()
    {
        var settings = AuthenticationSessionSettings.Create(
            new ConfigurationBuilder().Build(),
            isDevelopment: false);
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var properties = new AuthenticationProperties();
        properties.Items[AuthenticationSession.StartedAtProperty] = now.AddHours(-24)
            .ToUnixTimeSeconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(TimeSpan.FromHours(8), settings.IdleTimeout);
        Assert.Equal(TimeSpan.FromHours(24), settings.AbsoluteLifetime);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.SecurityStampValidationInterval);
        Assert.True(AuthenticationSession.IsWithinAbsoluteLifetime(properties, now, settings.AbsoluteLifetime));
        Assert.False(AuthenticationSession.IsWithinAbsoluteLifetime(
            properties,
            now.AddSeconds(1),
            settings.AbsoluteLifetime));

        var renewedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));
        AuthenticationSession.PreserveAuthenticationMethod(renewedPrincipal, "mfa");
        Assert.True(renewedPrincipal.HasClaim("amr", "mfa"));
        AuthenticationSession.PreserveAuthenticationMethod(renewedPrincipal, "untrusted");
        Assert.False(renewedPrincipal.HasClaim("amr", "untrusted"));
    }

    [Fact]
    public async Task AuthenticationRateLimits_AreIndependent()
    {
        Assert.Equal("login", AuthenticationRateLimits.GetRule("/Identity/Account/Login").Name);
        Assert.Equal("registration", AuthenticationRateLimits.GetRule("/Identity/Account/Register").Name);
        Assert.Equal("email-resend", AuthenticationRateLimits.GetRule("/Identity/Account/ResendEmailConfirmation").Name);
        Assert.Equal("recovery", AuthenticationRateLimits.GetRule("/Identity/Account/ForgotPassword").Name);
        Assert.Equal("recovery", AuthenticationRateLimits.GetRule("/Identity/Account/ResetPassword").Name);
        Assert.Equal("invitation", AuthenticationRateLimits.GetRule("/AcceptInvitation").Name);
        Assert.Equal("mfa", AuthenticationRateLimits.GetRule("/Identity/Account/LoginWith2fa").Name);
        Assert.Equal("mfa", AuthenticationRateLimits.GetRule("/Identity/Account/LoginWithRecoveryCode").Name);
        Assert.Equal("mfa", AuthenticationRateLimits.GetRule("/Identity/Account/Manage/Disable2fa").Name);
        Assert.Equal("recovery", AuthenticationRateLimits.GetRule("/Identity/Account/Manage/ChangePassword").Name);
        Assert.Equal(
            "login",
            AuthenticationRateLimits.FindRule("/Identity/Account/Login/")?.Name);

        using var factory = new Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        var loginRule = AuthenticationRateLimits.GetRule("/Identity/Account/Login");

        for (var attempt = 0; attempt < loginRule.PermitLimit; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/Identity/Account/Login");
            request.Content = new FormUrlEncodedContent([]);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.SendAsync(request)).StatusCode);
        }

        using var limitedRequest = new HttpRequestMessage(HttpMethod.Post, "/Identity/Account/Login");
        limitedRequest.Content = new FormUrlEncodedContent([]);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(limitedRequest)).StatusCode);

        using var independentRequest = new HttpRequestMessage(HttpMethod.Post, "/Identity/Account/Register")
        {
            Content = new FormUrlEncodedContent([])
        };
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.SendAsync(independentRequest)).StatusCode);
    }

    [Fact]
    public async Task ForwardedHeaders_TrustOnlyConfiguredProxies()
    {
        using var defaultFactory = new Factory();
        var defaults = defaultFactory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Empty(defaults.KnownProxies);
        Assert.Empty(defaults.KnownIPNetworks);

        var trustedProxy = IPAddress.Parse("10.0.0.8");
        var forwardedClient = IPAddress.Parse("198.51.100.20");
        var trusted = new ForwardedHeadersOptions();
        TrustedProxyConfiguration.Configure(trusted, [trustedProxy]);
        using var loggerFactory = LoggerFactory.Create(_ => { });

        var untrustedContext = CreateForwardedContext(IPAddress.Parse("192.0.2.10"), forwardedClient);
        await new ForwardedHeadersMiddleware(_ => Task.CompletedTask, loggerFactory, Options.Create(trusted))
            .Invoke(untrustedContext);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), untrustedContext.Connection.RemoteIpAddress);
        Assert.Equal("http", untrustedContext.Request.Scheme);

        var trustedContext = CreateForwardedContext(trustedProxy, forwardedClient);
        await new ForwardedHeadersMiddleware(_ => Task.CompletedTask, loggerFactory, Options.Create(trusted))
            .Invoke(trustedContext);
        Assert.Equal(forwardedClient, trustedContext.Connection.RemoteIpAddress);
        Assert.Equal("https", trustedContext.Request.Scheme);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:TrustedProxies:0"] = trustedProxy.ToString()
            })
            .Build();
        Assert.Equal(trustedProxy, Assert.Single(TrustedProxyConfiguration.Parse(configuration)));
    }

    private static DefaultHttpContext CreateForwardedContext(IPAddress remoteAddress, IPAddress forwardedAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = forwardedAddress.ToString();
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        return context;
    }

    private static ServiceProvider CreateIdentityProvider(bool useDevelopmentAuthenticationPolicy)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=unused_security_test"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCarbonFootprintInfrastructure(configuration, useDevelopmentAuthenticationPolicy);
        return services.BuildServiceProvider();
    }

    private static bool IsFreshAuthentication(
        string authenticationMethod,
        DateTimeOffset authenticatedAt,
        DateTimeOffset now)
    {
        var userId = Guid.NewGuid().ToString();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("amr", authenticationMethod)
            ],
            IdentityConstants.ApplicationScheme));
        var properties = new AuthenticationProperties();
        properties.Items[MfaAuthentication.AuthenticatedAtProperty] =
            authenticatedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        return MfaAuthentication.IsFresh(
            AuthenticateResult.Success(new AuthenticationTicket(
                principal,
                properties,
                IdentityConstants.ApplicationScheme)),
            principal,
            now,
            MfaAuthentication.DefaultFreshness,
            requireMfa: false);
    }

    private static async Task<bool> AuthorizeMfaAsync(
        string authenticationMethod,
        DateTimeOffset? verifiedAt,
        DateTimeOffset now)
    {
        var userId = Guid.NewGuid().ToString();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("amr", authenticationMethod)
            ],
            IdentityConstants.ApplicationScheme));
        var properties = new AuthenticationProperties();
        if (verifiedAt.HasValue)
        {
            properties.Items[MfaAuthentication.VerifiedAtProperty] =
                verifiedAt.Value.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var authentication = AuthenticateResult.Success(new AuthenticationTicket(
            principal,
            properties,
            IdentityConstants.ApplicationScheme));
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(new StubAuthenticationService(authentication))
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var requirement = new MfaEnabledRequirement();
        var authorization = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await new MfaEnabledHandler(
            new HttpContextAccessor { HttpContext = httpContext },
            new FixedTimeProvider(now),
            new MfaAuthenticationSettings(MfaAuthentication.DefaultFreshness)).HandleAsync(authorization);

        return authorization.HasSucceeded;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubAuthenticationService(AuthenticateResult result) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(result);

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task AnonymousUser_CanReadLiveness_ButCannotManageIdentity()
    {
        var health = await _client.GetAsync("/health/live");
        var manage = await _client.GetAsync("/Identity/Account/Manage");
        var workspace = await _client.GetAsync("/Workspace");
        var workspacePages = await Task.WhenAll(new[]
        {
            "/Workspace/product",
            "/Workspace/pcr",
            "/Workspace/inventory",
            "/Workspace/factors",
            "/Workspace/lifecycle",
            "/Workspace/lifecycle/raw-material",
            "/Workspace/lifecycle/manufacturing",
            "/Workspace/lifecycle/distribution",
            "/Workspace/lifecycle/use",
            "/Workspace/lifecycle/end-of-life",
            $"/Workspace/calculation?handler=ExportExcel&projectVersionId={Guid.NewGuid()}",
            "/Workspace/calculation"
        }.Select(path => _client.GetAsync(path)));
        var reports = await _client.GetAsync("/Reports");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, manage.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", manage.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Redirect, workspace.StatusCode);
        Assert.Equal("/Identity/Account/Login", workspace.Headers.Location?.AbsolutePath);
        Assert.All(workspacePages, response =>
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Identity/Account/Login", response.Headers.Location?.AbsolutePath);
        });
        Assert.Equal(HttpStatusCode.Redirect, reports.StatusCode);
        Assert.Equal("/Identity/Account/Login", reports.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Responses_IncludeBaselineSecurityAndCorrelationHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Correlation-ID", "security-test-correlation");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Contains("default-src 'self'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.Equal("security-test-correlation", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
    }

    [Fact]
    public void HardenedDeployment_RejectsDevelopmentAndInsecureDependencies()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Deployment:Hardened"] = "true",
            ["Deployment:SecretProvider"] = "test-provider",
            ["Security:RequireHttpsCookies"] = "true",
            ["https_port"] = "443",
            ["ConnectionStrings:Database"] = "Host=db;Database=carbon;SSL Mode=VerifyFull",
            ["ObjectStorage:Endpoint"] = "https://objects.example.com",
            ["Mail:EnableSsl"] = "true"
        };

        IConfiguration Configuration() => new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        DeploymentSecurity.Validate(Configuration(), "Production");
        Assert.Throws<InvalidOperationException>(() =>
            DeploymentSecurity.Validate(Configuration(), "Development"));

        settings["Deployment:Hardened"] = "false";
        settings["Security:RequireHttpsCookies"] = "false";
        Assert.Throws<InvalidOperationException>(() =>
            DeploymentSecurity.Validate(Configuration(), "Staging"));
        DeploymentSecurity.Validate(Configuration(), "Development");
        settings["Deployment:Hardened"] = "true";
        settings["Security:RequireHttpsCookies"] = "true";

        foreach (var key in new[]
                 {
                     "Deployment:SecretProvider",
                     "Security:RequireHttpsCookies",
                     "https_port",
                     "ConnectionStrings:Database",
                     "ObjectStorage:Endpoint",
                     "Mail:EnableSsl"
                 })
        {
            var value = settings[key];
            settings[key] = null;
            Assert.Throws<InvalidOperationException>(() =>
                DeploymentSecurity.Validate(Configuration(), "Production"));
            settings[key] = value;
        }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = "Host=localhost;Database=unused_security_test"
                });
            });
        }
    }

}
