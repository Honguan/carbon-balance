using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Infrastructure.Identity;

public sealed class SmtpEmailSender : IEmailSender<ApplicationUser>
{
    private readonly MailOptions _options;
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IOrganizationScope _organizationScope;
    private readonly IDataProtector _passwordProtector;

    public SmtpEmailSender(
        IOptions<MailOptions> options,
        CarbonFootprintDbContext dbContext,
        IOrganizationScope organizationScope,
        IDataProtectionProvider dataProtectionProvider)
    {
        _options = options.Value;
        _dbContext = dbContext;
        _organizationScope = organizationScope;
        _passwordProtector = dataProtectionProvider.CreateProtector("CarbonFootprint.OrganizationMailSettings.v1");
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

    public Task SendTestMessageAsync(string recipient) =>
        SendAsync(recipient, "碳足跡系統 SMTP 測試信", "此信件確認目前組織 SMTP 設定可正常寄送郵件。");

    private async Task SendAsync(string recipient, string subject, string htmlBody)
    {
        var settings = await ResolveSettingsAsync(CancellationToken.None);
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(recipient);

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl
        };
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }
        await client.SendMailAsync(message);
    }

    private async Task<ResolvedMailSettings> ResolveSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = _organizationScope.OrganizationId.HasValue
            ? await _dbContext.OrganizationMailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            : null;
        if (settings is null)
        {
            return new ResolvedMailSettings(
                _options.Host,
                _options.Port,
                _options.EnableSsl,
                _options.Username,
                _options.Password,
                _options.FromAddress,
                _options.FromName);
        }

        var password = string.IsNullOrWhiteSpace(settings.EncryptedPassword)
            ? string.Empty
            : _passwordProtector.Unprotect(settings.EncryptedPassword);
        return new ResolvedMailSettings(
            settings.Host,
            settings.Port,
            settings.EnableSsl,
            settings.Username,
            password,
            settings.FromAddress,
            settings.FromName);
    }

    private sealed record ResolvedMailSettings(
        string Host,
        int Port,
        bool EnableSsl,
        string Username,
        string Password,
        string FromAddress,
        string FromName);
}
