using System.ComponentModel.DataAnnotations;
using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Areas.Identity.Pages.Account;

public sealed class RegisterModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public RegisterModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (await _userManager.Users.AnyAsync())
        {
            TempData["AccountMessage"] = "系統已完成初始化，請使用既有帳號登入。";
            return RedirectToPage("./Login");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await _userManager.Users.AnyAsync())
        {
            TempData["AccountMessage"] = "系統已完成初始化，無法公開建立帳號。";
            return RedirectToPage("./Login");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = Input.DisplayName.Trim()
        };

        var createResult = await _userManager.CreateAsync(user, Input.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return Page();
        }

        foreach (var roleName in SystemRoles.All)
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await _roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!roleResult.Succeeded)
                {
                    AddIdentityErrors(roleResult);
                    await _userManager.DeleteAsync(user);
                    return Page();
                }
            }
        }

        var roleAssignment = await _userManager.AddToRoleAsync(user, SystemRoles.Administrator);
        if (!roleAssignment.Succeeded)
        {
            AddIdentityErrors(roleAssignment);
            await _userManager.DeleteAsync(user);
            return Page();
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToPage("/Workspace", new { area = string.Empty });
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            var message = error.Code switch
            {
                "DuplicateUserName" or "DuplicateEmail" => "此 Email 已被使用。",
                "PasswordTooShort" => "密碼至少需要 6 個字元。",
                _ => error.Description
            };
            ModelState.AddModelError(string.Empty, message);
        }
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "請輸入顯示名稱。")]
        [StringLength(80, ErrorMessage = "顯示名稱不可超過 80 個字元。")]
        [Display(Name = "顯示名稱")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入 Email。")]
        [EmailAddress(ErrorMessage = "Email 格式不正確。")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入密碼。")]
        [StringLength(128, MinimumLength = 6, ErrorMessage = "密碼至少需要 6 個字元。")]
        [DataType(DataType.Password)]
        [Display(Name = "密碼")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "請再次輸入密碼。")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "兩次輸入的密碼不相同。")]
        [Display(Name = "確認密碼")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
