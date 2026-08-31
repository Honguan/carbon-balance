using CarbonFootprint.Infrastructure;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CarbonFootprint.Integration.Tests;

public sealed class AdministratorBootstrapTests
{
    private const string Token = "integration-administrator-bootstrap-token-43";
    private const string EmailPrefix = "issue43-";

    [Fact]
    public async Task Bootstrap_IsControlledSingleUseAndAudited()
    {
        await using var provider = CreateProvider();
        await ResetAsync(provider);
        try
        {
            await EnsureRolesAsync(provider);

            var attacker = await RegisterAsync(provider, "attacker", null, "ordinary-attacker");
            Assert.True(attacker.Succeeded);
            Assert.False(attacker.IsAdministrator);
            Assert.True(await IsInRoleAsync(provider, attacker.User!.Id, SystemRoles.Viewer));
            Assert.False(await IsInRoleAsync(provider, attacker.User.Id, SystemRoles.Administrator));

            var invalid = await RegisterAsync(provider, "invalid", "wrong-token", "invalid-token");
            Assert.Equal(AccountRegistrationOutcome.InvalidBootstrapToken, invalid.Outcome);
            Assert.Null(invalid.User);

            var attempts = await Task.WhenAll(
                RegisterAsync(provider, "concurrent-a", Token, "bootstrap-a"),
                RegisterAsync(provider, "concurrent-b", Token, "bootstrap-b"));
            var winner = Assert.Single(attempts, result => result.Succeeded);
            Assert.True(winner.IsAdministrator);
            Assert.Equal(
                AccountRegistrationOutcome.BootstrapClosed,
                Assert.Single(attempts, result => !result.Succeeded).Outcome);

            var replay = await RegisterAsync(provider, "replay", Token, "bootstrap-replay");
            Assert.Equal(AccountRegistrationOutcome.BootstrapClosed, replay.Outcome);
            Assert.Null(replay.User);

            var target = await RegisterAsync(provider, "grant-target", null, "ordinary-target");
            Assert.True(target.Succeeded);
            var securityStampBeforeGrant = await GetSecurityStampAsync(provider, target.User!.Id);
            var unauthorized = await GrantAsync(
                provider,
                attacker.User.Id,
                target.User.Id,
                "unauthorized-grant");
            Assert.False(unauthorized.Succeeded);

            var granted = await GrantAsync(
                provider,
                winner.User!.Id,
                target.User.Id,
                "authorized-grant");
            Assert.True(granted.Succeeded);
            Assert.True(await IsInRoleAsync(provider, target.User.Id, SystemRoles.Administrator));
            Assert.NotEqual(securityStampBeforeGrant, await GetSecurityStampAsync(provider, target.User.Id));

            var recoveryCode = await GenerateRecoveryCodeAsync(provider, target.User.Id);
            var stampBeforeRecoveryCodeRedemption = await GetSecurityStampAsync(provider, target.User.Id);
            Assert.True(await RedeemRecoveryCodeAsync(provider, target.User.Id, recoveryCode));
            Assert.NotEqual(
                stampBeforeRecoveryCodeRedemption,
                await GetSecurityStampAsync(provider, target.User.Id));

            await using var verificationScope = provider.CreateAsyncScope();
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<CarbonFootprintDbContext>();
            var claim = await dbContext.AdministratorBootstrap.AsNoTracking().SingleAsync();
            Assert.Equal(winner.User.Id, claim.ClaimedByUserId);
            Assert.Equal("public-registration-bootstrap-token", claim.Source);
            Assert.Contains(claim.CorrelationId, new[] { "bootstrap-a", "bootstrap-b" });

            var audits = await dbContext.SystemAuditEvents
                .AsNoTracking()
                .OrderBy(item => item.Timestamp)
                .ToArrayAsync();
            Assert.Collection(
                audits,
                bootstrapAudit =>
                {
                    Assert.Equal("identity.administrator.bootstrapped", bootstrapAudit.Action);
                    Assert.Equal(winner.User.Id, bootstrapAudit.ActorId);
                    Assert.Equal(winner.User.Id, bootstrapAudit.ResourceId);
                    Assert.Equal("public-registration-bootstrap-token", bootstrapAudit.Source);
                    Assert.Contains(bootstrapAudit.CorrelationId, new[] { "bootstrap-a", "bootstrap-b" });
                },
                grantAudit =>
                {
                    Assert.Equal("identity.administrator.granted", grantAudit.Action);
                    Assert.Equal(winner.User.Id, grantAudit.ActorId);
                    Assert.Equal(target.User.Id, grantAudit.ResourceId);
                    Assert.Equal("authenticated-administrator", grantAudit.Source);
                    Assert.Equal("authorized-grant", grantAudit.CorrelationId);
                });
        }
        finally
        {
            await ResetAsync(provider);
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Integration test 需要 CARBON_TEST_DB_CONNECTION。");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = connectionString,
                ["AdministratorBootstrap:Token"] = Token
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCarbonFootprintInfrastructure(configuration, useDevelopmentAuthenticationPolicy: true);
        return services.BuildServiceProvider();
    }

    private static async Task EnsureRolesAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in SystemRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
            }
        }
    }

    private static async Task<AccountRegistrationResult> RegisterAsync(
        ServiceProvider provider,
        string name,
        string? token,
        string correlationId)
    {
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SystemAdministratorService>();
        var email = $"{EmailPrefix}{name}@example.test";
        return await service.RegisterAsync(
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                DisplayName = name
            },
            "carbon1",
            token,
            correlationId,
            CancellationToken.None);
    }

    private static async Task<IdentityResult> GrantAsync(
        ServiceProvider provider,
        Guid actorId,
        Guid targetId,
        string correlationId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<SystemAdministratorService>()
            .GrantAdministratorAsync(actorId, targetId, correlationId, CancellationToken.None);
    }

    private static async Task<bool> IsInRoleAsync(
        ServiceProvider provider,
        Guid userId,
        string role)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is not null && await userManager.IsInRoleAsync(user, role);
    }

    private static async Task<string?> GetSecurityStampAsync(ServiceProvider provider, Guid userId)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await userManager.GetSecurityStampAsync(user);
    }

    private static async Task<string> GenerateRecoveryCodeAsync(ServiceProvider provider, Guid userId)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.IsType<SecurityStampUserManager>(userManager);
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException();
        return Assert.Single(await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 1) ?? []);
    }

    private static async Task<bool> RedeemRecoveryCodeAsync(
        ServiceProvider provider,
        Guid userId,
        string recoveryCode)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException();
        return (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode)).Succeeded;
    }

    private static async Task ResetAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CarbonFootprintDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM identity.administrator_bootstrap");
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM identity.system_audit_events");
        await dbContext.Database.ExecuteSqlRawAsync($$"""
            DELETE FROM identity.user_roles
            WHERE user_id IN (
                SELECT id FROM identity.users WHERE email LIKE '{{EmailPrefix}}%'
            )
            """);
        await dbContext.Database.ExecuteSqlRawAsync($$"""
            DELETE FROM identity.users WHERE email LIKE '{{EmailPrefix}}%'
            """);
    }
}
