using System.ComponentModel.DataAnnotations;
using System.Text;
using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace CarbonFootprint.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class ResendEmailConfirmationModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender<ApplicationUser> _emailSender;

    public ResendEmailConfirmationModel(
        UserManager<ApplicationUser> userManager,
        IEmailSender<ApplicationUser> emailSender)
    {
        _userManager = userManager;
        _emailSender = emailSender;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? StatusMessage { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || await _userManager.IsEmailConfirmedAsync(user))
        {
            StatusMessage = "若此 Email 尚未確認，系統已重新寄送確認信。";
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
            ModelState.AddModelError(string.Empty, "無法產生確認連結，請稍後再試。");
            return Page();
        }

        try
        {
            await _emailSender.SendConfirmationLinkAsync(user, email, confirmationLink);
            StatusMessage = "確認信已寄出，請前往信箱開啟確認連結。";
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "確認信寄送失敗，請確認郵件服務後再試。");
        }

        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "請輸入 Email。")]
        [EmailAddress(ErrorMessage = "Email 格式不正確。")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }
}
