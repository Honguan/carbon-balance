using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Infrastructure.Identity;

public sealed class SecurityStampUserManager(
    IUserStore<ApplicationUser> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<ApplicationUser>> logger)
    : UserManager<ApplicationUser>(
        store,
        optionsAccessor,
        passwordHasher,
        userValidators,
        passwordValidators,
        keyNormalizer,
        errors,
        services,
        logger)
{
    public override async Task<IdentityResult> RedeemTwoFactorRecoveryCodeAsync(
        ApplicationUser user,
        string code)
    {
        var result = await base.RedeemTwoFactorRecoveryCodeAsync(user, code);
        if (result.Succeeded)
        {
            EnsureSucceeded(await UpdateSecurityStampAsync(user));
        }

        return result;
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("無法更新登入安全狀態。");
        }
    }
}
