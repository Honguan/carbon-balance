using System.Globalization;
using System.Threading.RateLimiting;
using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Infrastructure;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Security;
using CarbonFootprint.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var administratorBootstrapToken = builder.Configuration[$"{AdministratorBootstrapOptions.SectionName}:Token"];
if (!builder.Environment.IsDevelopment()
    && !AdministratorBootstrapOptions.IsValidProductionToken(administratorBootstrapToken))
{
    throw new InvalidOperationException(
        "正式環境必須設定 32 至 128 個字元的 AdministratorBootstrap:Token。");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
var mfaFreshness = builder.Configuration.GetValue<TimeSpan?>("Security:MfaFreshness")
    ?? MfaAuthentication.DefaultFreshness;
if (mfaFreshness <= TimeSpan.Zero)
{
    throw new InvalidOperationException("Security:MfaFreshness 必須大於零。");
}
builder.Services.AddSingleton(new MfaAuthenticationSettings(mfaFreshness));
builder.Services.AddScoped<IOrganizationScope, HttpOrganizationScope>();
builder.Services.AddCarbonFootprintInfrastructure(builder.Configuration);
builder.Services.ConfigureApplicationCookie(options =>
{
    var previousOnSigningIn = options.Events.OnSigningIn;
    options.Events.OnSigningIn = async context =>
    {
        await previousOnSigningIn(context);
        if (context.Principal?.HasClaim(claim => claim.Type == "amr" && claim.Value is "pwd" or "mfa") == true)
        {
            var authenticatedAt = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>()
                .GetUtcNow()
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
            context.Properties.Items.TryAdd(MfaAuthentication.AuthenticatedAtProperty, authenticatedAt);
            if (context.Principal.HasClaim("amr", "mfa"))
            {
                context.Properties.Items.TryAdd(
                    MfaAuthentication.VerifiedAtProperty,
                    authenticatedAt);
            }
        }
    };
});
builder.Services.AddSingleton<CalculationEngine>();
builder.Services.AddScoped<CalculateInventoryHandler>();
builder.Services.Configure<MoenvFactorSourceOptions>(
    builder.Configuration.GetSection(MoenvFactorSourceOptions.SectionName));
builder.Services.AddHttpClient<IMoenvFactorSource, MoenvFactorClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<MoenvFactorSynchronizationService>();
builder.Services.AddScoped<IAuthorizationHandler, OrganizationPermissionHandler>();
builder.Services.AddScoped<IAuthorizationHandler, MfaEnabledHandler>();
builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
var dataProtectionPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionPath))
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "正式環境必須設定 DataProtection:KeyPath，才能持久化組織 SMTP 密碼的加密金鑰。");
}
builder.Services.AddHealthChecks().AddDbContextCheck<CarbonFootprintDbContext>("postgresql");
var rateLimitPermitCount = builder.Configuration.GetValue("RateLimiting:PermitLimit", 120);
if (rateLimitPermitCount <= 0)
{
    throw new InvalidOperationException("RateLimiting:PermitLimit 必須大於零。");
}
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitPermitCount,
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

    var moenvOptions = builder.Configuration
        .GetSection(MoenvFactorSourceOptions.SectionName)
        .Get<MoenvFactorSourceOptions>() ?? new MoenvFactorSourceOptions();
    if (moenvOptions.ImportOnDeployment)
    {
        var synchronizationService = scope.ServiceProvider
            .GetRequiredService<MoenvFactorSynchronizationService>();
        var result = await synchronizationService.SynchronizeExistingOrganizationsAsync(
            $"deployment-{Guid.NewGuid():N}",
            CancellationToken.None);
        app.Logger.LogInformation(
            "Deployment MOENV factor import completed for {OrganizationCount} organizations: {CreatedCount} created and published, {PublishedExistingCount} existing drafts published, {UnchangedCount} unchanged, {SkippedCount} skipped.",
            result.OrganizationCount,
            result.CreatedCount,
            result.PublishedExistingCount,
            result.UnchangedCount,
            result.SkippedCount);
    }

    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    if (httpContext.Response.StatusCode == StatusCodes.Status400BadRequest
        && httpContext.Request.Path.StartsWithSegments("/Identity/Account")
        && !httpContext.Response.HasStarted)
    {
        httpContext.Response.Redirect("/Identity/Account/Login?requestExpired=true");
        return;
    }

    await Task.CompletedTask;
});

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
    "/Identity/Account/ConfirmEmailChange",
    "/Identity/Account/ExternalLogin",
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
app.Use(async (context, next) =>
{
    if ((context.Request.Path.Equals("/Identity/Account/LoginWith2fa")
            || context.Request.Path.Equals("/Identity/Account/LoginWithRecoveryCode"))
        && !(await context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme)).Succeeded)
    {
        context.Response.Redirect("/Identity/Account/Login");
        return;
    }

    if (context.Request.Path.StartsWithSegments("/Identity/Account/Manage"))
    {
        var authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!MfaAuthentication.IsFresh(
            authentication,
            context.User,
            context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
            context.RequestServices.GetRequiredService<MfaAuthenticationSettings>().Freshness,
            requireMfa: false))
        {
            var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
            context.Response.Redirect($"/Identity/Account/Login?returnUrl={returnUrl}");
            return;
        }
    }

    await next(context);
});
app.UseAuthorization();
app.MapStaticAssets();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapRazorPages().WithStaticAssets();
app.Run();

public partial class Program;
