using System.Text;
using Microsoft.AspNetCore.Identity;

namespace CarbonFootprint.Infrastructure.Identity;

public sealed class CompromisedPasswordValidator : IPasswordValidator<ApplicationUser>
{
    private static readonly string[] CompromisedPasswordRoots =
    {
        "123456",
        "ADMIN",
        "ADMINISTRATOR",
        "BASEBALL",
        "DRAGON",
        "FOOTBALL",
        "ILOVEYOU",
        "LETMEIN",
        "LETMEINPLEASE",
        "MICHAEL",
        "MONKEY",
        "PASSWORD",
        "PRINCESS",
        "QWERTY",
        "QWERTYUIOP",
        "SUNSHINE",
        "WELCOME"
    };

    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        var normalized = password?.Normalize(NormalizationForm.FormKC).ToUpperInvariant() ?? string.Empty;
        var compromised = CompromisedPasswordRoots.Any(root => IsPredictableVariation(normalized, root))
            || (normalized.Length > 0 && normalized.All(character => character == normalized[0]));
        return Task.FromResult(compromised
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "CompromisedPassword",
                Description = "此密碼過於常見，請改用不重複使用的長密語。"
            })
            : IdentityResult.Success);
    }

    private static bool IsPredictableVariation(string password, string root)
    {
        if (!password.StartsWith(root, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = password[root.Length..];
        return suffix.Length == 0
            || suffix.All(character => !char.IsLetter(character))
            || suffix.StartsWith(root, StringComparison.Ordinal);
    }
}
