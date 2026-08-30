using System.Net;
using System.Security.Claims;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
