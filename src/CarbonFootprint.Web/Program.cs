using System.Threading.RateLimiting;
using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Infrastructure;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IOrganizationScope, HttpOrganizationScope>();
builder.Services.AddCarbonFootprintInfrastructure(builder.Configuration);
builder.Services.AddSingleton<CalculationEngine>();
builder.Services.AddScoped<CalculateInventoryHandler>();
builder.Services.AddScoped<IAuthorizationHandler, OrganizationPermissionHandler>();
builder.Services.AddScoped<IAuthorizationHandler, MfaEnabledHandler>();
builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
var dataProtectionPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionPath))
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
}
builder.Services.AddHealthChecks().AddDbContextCheck<CarbonFootprintDbContext>("postgresql");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("carbon-footprint-web"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        metrics.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            metrics.AddOtlpExporter();
        }
    });

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CarbonFootprintDbContext>();
    await dbContext.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var roleName in SystemRoles.All)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            if (!roleResult.Succeeded)
            {
                var errors = string.Join(", ", roleResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"無法建立系統角色 {roleName}: {errors}");
            }
        }
    }

    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    const string headerName = "X-Correlation-ID";
    var correlationId = context.Request.Headers[headerName].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 100)
    {
        correlationId = Guid.NewGuid().ToString("N");
    }

    context.TraceIdentifier = correlationId;
    context.Response.Headers[headerName] = correlationId;
    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next(context);
    }
});

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'";
    await next(context);
});

var disabledIdentityPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "/Identity/Account/ForgotPassword",
    "/Identity/Account/ForgotPasswordConfirmation",
    "/Identity/Account/ResetPassword",
    "/Identity/Account/ResetPasswordConfirmation",
    "/Identity/Account/ConfirmEmailChange",
    "/Identity/Account/ExternalLogin",
    "/Identity/Account/LoginWith2fa",
    "/Identity/Account/LoginWithRecoveryCode",
    "/Identity/Account/Manage/TwoFactorAuthentication",
    "/Identity/Account/Manage/EnableAuthenticator",
    "/Identity/Account/Manage/ResetAuthenticator",
    "/Identity/Account/Manage/GenerateRecoveryCodes",
    "/Identity/Account/Manage/ExternalLogins"
};

app.Use(async (context, next) =>
{
    if (context.Request.Path.Value is { } path && disabledIdentityPaths.Contains(path))
    {
        context.Response.Redirect("/Identity/Account/Login");
        return;
    }

    await next(context);
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapRazorPages().WithStaticAssets();
app.Run();

public partial class Program;
