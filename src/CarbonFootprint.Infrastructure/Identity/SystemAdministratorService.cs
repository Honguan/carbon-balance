using System.Security.Cryptography;
using System.Text;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Infrastructure.Identity;

public enum AccountRegistrationOutcome
{
    Succeeded,
    InvalidBootstrapToken,
    BootstrapClosed,
    IdentityFailure
}

public sealed record AccountRegistrationResult(
    AccountRegistrationOutcome Outcome,
    ApplicationUser? User,
    bool IsAdministrator,
    IReadOnlyCollection<IdentityError> Errors)
{
    public bool Succeeded => Outcome == AccountRegistrationOutcome.Succeeded;
}

public sealed class SystemAdministratorService
{
    private const int BootstrapSingletonId = 1;
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AdministratorBootstrapOptions _options;

    public SystemAdministratorService(
        CarbonFootprintDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IOptions<AdministratorBootstrapOptions> options)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _options = options.Value;
    }

    public Task<bool> IsBootstrapOpenAsync(CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(_options.Token)
            ? Task.FromResult(false)
            : _dbContext.AdministratorBootstrap
                .AsNoTracking()
                .AllAsync(item => item.Id != BootstrapSingletonId, cancellationToken);

    public async Task<AccountRegistrationResult> RegisterAsync(
        ApplicationUser user,
        string password,
        string? bootstrapToken,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bootstrapToken))
        {
            return await RegisterViewerAsync(user, password);
        }

        if (!TokenMatches(bootstrapToken))
        {
            return new(
                AccountRegistrationOutcome.InvalidBootstrapToken,
                null,
                false,
                []);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var claimedAt = DateTimeOffset.UtcNow;
        var claimed = await _dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO identity.administrator_bootstrap
                (id, claimed_by_user_id, claimed_at, source, correlation_id)
            VALUES
                ({{BootstrapSingletonId}}, {{user.Id}}, {{claimedAt}}, 'public-registration-bootstrap-token', {{correlationId}})
            ON CONFLICT (id) DO NOTHING
            """, cancellationToken);
        if (claimed == 0)
        {
            return new(AccountRegistrationOutcome.BootstrapClosed, null, false, []);
        }

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return Failure(createResult);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, SystemRoles.Administrator);
        if (!roleResult.Succeeded)
        {
            return Failure(roleResult);
        }

        _dbContext.SystemAuditEvents.Add(new SystemAuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = claimedAt,
            ActorId = user.Id,
            Action = "identity.administrator.bootstrapped",
            ResourceType = nameof(ApplicationUser),
            ResourceId = user.Id,
            Source = "public-registration-bootstrap-token",
            CorrelationId = correlationId,
            MetadataJson = "{}"
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(AccountRegistrationOutcome.Succeeded, user, true, []);
    }

    public async Task<IdentityResult> GrantAdministratorAsync(
        Guid actorId,
        Guid targetUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = await _userManager.FindByIdAsync(actorId.ToString());
        var target = await _userManager.FindByIdAsync(targetUserId.ToString());
        if (actor is null || target is null || !await _userManager.IsInRoleAsync(actor, SystemRoles.Administrator))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "AdministratorAuthorizationRequired",
                Description = "只有系統管理者可以指派系統管理者角色。"
            });
        }

        if (await _userManager.IsInRoleAsync(target, SystemRoles.Administrator))
        {
            return IdentityResult.Success;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var result = await _userManager.AddToRoleAsync(target, SystemRoles.Administrator);
        if (!result.Succeeded)
        {
            return result;
        }

        var stampResult = await _userManager.UpdateSecurityStampAsync(target);
        if (!stampResult.Succeeded)
        {
            return stampResult;
        }

        _dbContext.SystemAuditEvents.Add(new SystemAuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actor.Id,
            Action = "identity.administrator.granted",
            ResourceType = nameof(ApplicationUser),
            ResourceId = target.Id,
            Source = "authenticated-administrator",
            CorrelationId = correlationId,
            MetadataJson = "{}"
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IdentityResult.Success;
    }

    private async Task<AccountRegistrationResult> RegisterViewerAsync(ApplicationUser user, string password)
    {
        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return Failure(createResult);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, SystemRoles.Viewer);
        if (roleResult.Succeeded)
        {
            return new(AccountRegistrationOutcome.Succeeded, user, false, []);
        }

        await _userManager.DeleteAsync(user);
        return Failure(roleResult);
    }

    private bool TokenMatches(string candidate)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            return false;
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(_options.Token));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(candidate));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static AccountRegistrationResult Failure(IdentityResult result) =>
        new(AccountRegistrationOutcome.IdentityFailure, null, false, result.Errors.ToArray());
}
