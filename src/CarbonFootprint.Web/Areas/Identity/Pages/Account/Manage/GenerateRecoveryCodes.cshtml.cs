using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarbonFootprint.Web.Areas.Identity.Pages.Account.Manage;

[Authorize]
public sealed class GenerateRecoveryCodesModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<GenerateRecoveryCodesModel> logger) : PageModel
{
    [TempData]
    public string[]? RecoveryCodes { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        return await userManager.GetTwoFactorEnabledAsync(user)
            ? Page()
            : BadRequest();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }
        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return BadRequest();
        }

        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "無法更新登入安全狀態，請稍後再試。");
            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        RecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray()
            ?? throw new InvalidOperationException("無法產生復原碼。");
        StatusMessage = "已產生新的復原碼。";
        logger.LogInformation("User generated new two-factor recovery codes.");
        return RedirectToPage("./ShowRecoveryCodes");
    }
}
