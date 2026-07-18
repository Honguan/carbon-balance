using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Evidence;
using CarbonFootprint.Infrastructure.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CarbonFootprint.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCarbonFootprintInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Database 設定。");
        services.AddDbContext<CarbonFootprintDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.TryAddScoped<IOrganizationScope, UnscopedOrganizationScope>();
        services.AddScoped<ICalculationRunStore, CalculationRunStore>();
        services.AddScoped<OrganizationOnboardingService>();
        services.AddScoped<OrganizationInvitationService>();
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));
        services.Configure<MalwareScannerOptions>(configuration.GetSection(MalwareScannerOptions.SectionName));
        services.AddScoped<ClamAvMalwareScanner>();
        services.AddScoped<EvidenceStorageService>();
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.SectionName));
        services.AddScoped<SmtpEmailSender>();
        services.AddScoped<IEmailSender<ApplicationUser>>(provider => provider.GetRequiredService<SmtpEmailSender>());
        services.AddDefaultIdentity<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CarbonFootprintDbContext>();
        return services;
    }
}
