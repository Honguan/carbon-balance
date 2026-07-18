using System.Text;
using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace CarbonFootprint.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class ConfirmEmailModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ConfirmEmailModel(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public bool Succeeded { get; private set; }

    public string Message { get; private set; } = "確認連結無效，請重新寄送確認信。";

    public async Task OnGetAsync(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            Message = "確認連結不完整，請回到登入頁重新寄送確認信。";
            return;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            Message = "找不到此帳號，請確認使用的是最新確認信。";
            return;
        }

        if (await _userManager.IsEmailConfirmedAsync(user))
        {
            Succeeded = true;
            Message = "此 Email 已完成確認，可以直接登入。";
            return;
        }

        try
        {
            var decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _userManager.ConfirmEmailAsync(user, decodedCode);
            Succeeded = result.Succeeded;
            Message = result.Succeeded
                ? "帳號已啟用，現在可以登入碳足跡系統。"
                : "確認連結可能已過期，請重新寄送確認信。";
        }
        catch (FormatException)
        {
            Message = "確認連結格式不正確，請重新寄送確認信。";
        }
    }
}
