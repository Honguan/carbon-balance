using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
    public async Task AnonymousUser_CanReadLiveness_ButCannotManageIdentity()
    {
        var health = await _client.GetAsync("/health/live");
        var manage = await _client.GetAsync("/Identity/Account/Manage");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, manage.StatusCode);
        Assert.Equal("/Identity/Account/Login", manage.Headers.Location?.AbsolutePath);
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
