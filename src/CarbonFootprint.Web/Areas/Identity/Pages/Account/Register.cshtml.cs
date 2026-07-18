using System.ComponentModel.DataAnnotations;
using System.Text;
using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace CarbonFootprint.Web.Areas.Identity.Pages.Account;

public sealed class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IEmailSender<ApplicationUser> _emailSender;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IEmailSender<ApplicationUser> emailSender)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _emailSender = emailSender;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool CreatesAdministrator { get; private set; }

    public async Task OnGetAsync()
    {
        await EnsureRolesAsync();
        CreatesAdministrator = !await HasAdministratorAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await EnsureRolesAsync();
        CreatesAdministrator = !await HasAdministratorAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            DisplayName = Input.DisplayName.Trim()
        };

        var createResult = await _userManager.CreateAsync(user, Input.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return Page();
        }

        var assignedRole = CreatesAdministrator ? SystemRoles.Administrator : SystemRoles.Viewer;
        var roleAssignment = await _userManager.AddToRoleAsync(user, assignedRole);
        if (!roleAssignment.Succeeded)
        {
            AddIdentityErrors(roleAssignment);
            await _userManager.DeleteAsync(user);
            return Page();
        }

        var confirmationCode = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        confirmationCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(confirmationCode));
        var confirmationLink = Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { area = "Identity", userId = user.Id, code = confirmationCode },
            protocol: Request.Scheme);

        if (string.IsNullOrWhiteSpace(confirmationLink))
        {
            TempData["AccountMessage"] = "帳號已建立，但無法產生確認連結。請使用重寄確認信功能。";
            return RedirectToPage("./Login");
        }

        try
        {
            await _emailSender.SendConfirmationLinkAsync(user, email, confirmationLink);
            TempData["AccountMessage"] = CreatesAdministrator
                ? "初始管理者已建立。請先至信箱完成 Email 確認，再登入系統。"
                : "帳號已建立。請先至信箱完成 Email 確認，再登入系統。";
        }
        catch
        {
            TempData["AccountMessage"] = "帳號已建立，但確認信寄送失敗。請使用重寄確認信功能。";
        }

        return RedirectToPage("./Login");
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var roleName in SystemRoles.All)
        {
            if (await _roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var roleResult = await _roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join(", ", roleResult.Errors.Select(error => error.Description)));
            }
        }
    }

    private async Task<bool> HasAdministratorAsync()
    {
        var administrators = await _userManager.GetUsersInRoleAsync(SystemRoles.Administrator);
        return administrators.Count > 0;
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
