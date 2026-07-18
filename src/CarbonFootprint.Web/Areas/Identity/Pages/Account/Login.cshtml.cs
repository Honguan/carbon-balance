using System.ComponentModel.DataAnnotations;
using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarbonFootprint.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(bool requestExpired = false)
    {
        // The anti-forgery token is bound to the current identity. After an
        // explicit sign-out, redirect once so the login form and token are
        // rendered in a fresh anonymous request.
        if (_signInManager.IsSignedIn(User))
        {
            await _signInManager.SignOutAsync();
            TempData["AccountMessage"] = "已清除原有登入狀態，請重新輸入帳號與密碼。";
            return RedirectToPage("./Login", new { returnUrl = ReturnUrl });
        }

        if (requestExpired)
        {
            TempData["AccountMessage"] = "頁面已更新或表單已逾時，請重新操作。";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (_signInManager.IsSignedIn(User))
        {
            await _signInManager.SignOutAsync();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var identifier = Input.Identifier.Trim();
        var user = await _userManager.FindByNameAsync(identifier)
            ?? await _userManager.FindByEmailAsync(identifier);

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "帳號或密碼不正確。");
            return Page();
        }

        var passwordResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            Input.Password,
            lockoutOnFailure: true);

        if (passwordResult.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "登入嘗試次數過多，請在 5 分鐘後再試。");
            return Page();
        }

        if (passwordResult.IsNotAllowed)
        {
            ModelState.AddModelError(string.Empty, "此帳號尚未完成 Email 確認，請先開啟確認信或重新寄送確認信。");
            return Page();
        }

        if (!passwordResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "帳號或密碼不正確。");
            return Page();
        }

        var authenticationProperties = new AuthenticationProperties
        {
            IsPersistent = Input.RememberMe,
            AllowRefresh = true
        };
        if (Input.RememberMe)
        {
            authenticationProperties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
        }

        await _signInManager.SignInAsync(user, authenticationProperties);

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage("/Workspace", new { area = string.Empty });
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "請輸入帳號或 Email。")]
        [Display(Name = "帳號或 Email")]
        public string Identifier { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入密碼。")]
        [DataType(DataType.Password)]
        [Display(Name = "密碼")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "保持登入")]
        public bool RememberMe { get; set; }
    }
}
