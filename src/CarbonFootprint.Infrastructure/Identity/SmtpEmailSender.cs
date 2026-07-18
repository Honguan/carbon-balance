using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Infrastructure.Identity;

public sealed class SmtpEmailSender : IEmailSender<ApplicationUser>
{
    private readonly MailOptions _options;

    public SmtpEmailSender(IOptions<MailOptions> options)
    {
        _options = options.Value;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "確認產品碳足跡系統帳號", $"請開啟下列連結確認帳號：<a href=\"{HtmlEncoder.Default.Encode(confirmationLink)}\">確認帳號</a>");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "重設產品碳足跡系統密碼", $"請開啟下列連結重設密碼：<a href=\"{HtmlEncoder.Default.Encode(resetLink)}\">重設密碼</a>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "產品碳足跡系統密碼重設碼", $"密碼重設碼：<strong>{HtmlEncoder.Default.Encode(resetCode)}</strong>");

    public Task SendOrganizationInvitationAsync(string email, string invitationLink) =>
        SendAsync(
            email,
            "產品碳足跡系統組織邀請",
            $"請在七日內使用受邀 Email 登入並接受邀請：<a href=\"{HtmlEncoder.Default.Encode(invitationLink)}\">接受邀請</a>");

    private async Task SendAsync(string recipient, string subject, string htmlBody)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(recipient);

        using var client = new SmtpClient(_options.Host, _options.Port);
        await client.SendMailAsync(message);
    }
}
