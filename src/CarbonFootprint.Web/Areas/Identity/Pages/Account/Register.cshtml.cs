using System.ComponentModel.DataAnnotations;
using System.Text;
using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SystemAdministratorService _systemAdministratorService;
    private readonly IEmailSender<ApplicationUser> _emailSender;
    private readonly IdentityOptions _identityOptions;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SystemAdministratorService systemAdministratorService,
        IEmailSender<ApplicationUser> emailSender,
        IOptions<IdentityOptions> identityOptions)
    {
        _userManager = userManager;
        _systemAdministratorService = systemAdministratorService;
        _emailSender = emailSender;
        _identityOptions = identityOptions.Value;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool BootstrapOpen { get; private set; }
    public int MinimumPasswordLength => _identityOptions.Password.RequiredLength;

    public async Task OnGetAsync()
    {
        BootstrapOpen = await _systemAdministratorService.IsBootstrapOpenAsync(HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        BootstrapOpen = await _systemAdministratorService.IsBootstrapOpenAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            DisplayName = Input.DisplayName.Trim()
        };

        var registration = await _systemAdministratorService.RegisterAsync(
            user,
            Input.Password,
            Input.BootstrapToken,
            HttpContext.TraceIdentifier,
            cancellationToken);
        if (!registration.Succeeded)
        {
            if (registration.Outcome == AccountRegistrationOutcome.InvalidBootstrapToken)
            {
                ModelState.AddModelError(nameof(Input.BootstrapToken), "Bootstrap token 無效。");
            }
            else if (registration.Outcome == AccountRegistrationOutcome.BootstrapClosed)
            {
                BootstrapOpen = false;
                ModelState.AddModelError(nameof(Input.BootstrapToken), "初始管理者 bootstrap 已永久關閉。");
            }
            else
            {
                AddIdentityErrors(IdentityResult.Failed(registration.Errors.ToArray()));
            }
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
            TempData["AccountMessage"] = registration.IsAdministrator
                ? "初始管理者已建立。請先至信箱完成 Email 確認，再登入系統。"
                : "帳號已建立。請先至信箱完成 Email 確認，再登入系統。";
        }
        catch
        {
            TempData["AccountMessage"] = "帳號已建立，但確認信寄送失敗。請使用重寄確認信功能。";
        }

        return RedirectToPage("./Login");
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            var message = error.Code switch
            {
                "DuplicateUserName" or "DuplicateEmail" => "此 Email 已註冊；若尚未確認，請使用重寄確認信功能。",
                "PasswordTooShort" => $"密碼至少需要 {MinimumPasswordLength} 個字元。",
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
        [StringLength(128, ErrorMessage = "密碼不可超過 128 個字元。")]
        [DataType(DataType.Password)]
        [Display(Name = "密碼")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "請再次輸入密碼。")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "兩次輸入的密碼不相同。")]
        [Display(Name = "確認密碼")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [StringLength(128, ErrorMessage = "Bootstrap token 不可超過 128 個字元。")]
        [DataType(DataType.Password)]
        [Display(Name = "初始管理者 Bootstrap token（選填）")]
        public string? BootstrapToken { get; set; }
    }
}
