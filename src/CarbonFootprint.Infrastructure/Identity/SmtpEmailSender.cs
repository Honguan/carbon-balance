using System.Net.Sockets;
using System.Text.Encodings.Web;
using MailKit.Net.Smtp;
using MimeKit;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Infrastructure.Identity;

public sealed class SmtpEmailSender : IEmailSender<ApplicationUser>
{
    private readonly MailOptions _options;
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IOrganizationScope _organizationScope;
    private readonly IDataProtector _passwordProtector;
    private readonly SmtpEgressPolicy _egressPolicy;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SmtpEmailSender(
        IOptions<MailOptions> options,
        CarbonFootprintDbContext dbContext,
        IOrganizationScope organizationScope,
        IDataProtectionProvider dataProtectionProvider,
        SmtpEgressPolicy egressPolicy,
        IHttpContextAccessor httpContextAccessor)
    {
        _options = options.Value;
        _dbContext = dbContext;
        _organizationScope = organizationScope;
        _passwordProtector = dataProtectionProvider.CreateProtector("CarbonFootprint.OrganizationMailSettings.v1");
        _egressPolicy = egressPolicy;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "確認產品碳足跡系統帳號", $"請開啟下列連結確認帳號：<a href=\"{HtmlEncoder.Default.Encode(confirmationLink)}\">確認帳號</a>");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "重設產品碳足跡系統密碼", $"請開啟下列連結重設密碼：<a href=\"{HtmlEncoder.Default.Encode(resetLink)}\">重設密碼</a>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "產品碳足跡系統密碼重設碼", $"密碼重設碼：<strong>{HtmlEncoder.Default.Encode(resetCode)}</strong>");

    public Task SendOrganizationInvitationAsync(string email, string invitationLink, CancellationToken cancellationToken = default) =>
        SendAsync(
            email,
            "產品碳足跡系統組織邀請",
            $"請在七日內使用受邀 Email 登入並接受邀請：<a href=\"{HtmlEncoder.Default.Encode(invitationLink)}\">接受邀請</a>", cancellationToken);

    public Task SendTestMessageAsync(string recipient, CancellationToken cancellationToken = default) =>
        SendAsync(recipient, "碳足跡系統 SMTP 測試信", "此信件確認目前組織 SMTP 設定可正常寄送郵件。", cancellationToken);

    public async Task ValidateSettingsAsync(string host, int port, bool enableSsl, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_egressPolicy.Timeout);
        try
        {
            await _egressPolicy.ValidateAsync(host, port, enableSsl, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("SMTP 目的地驗證逾時。");
        }
        catch (SocketException)
        {
            throw new InvalidOperationException("SMTP 主機 DNS 解析失敗。");
        }
    }

    private async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
        deadline.CancelAfter(_egressPolicy.Timeout);
        var token = deadline.Token;
        try
        {
            var settings = await ResolveSettingsAsync(token);
            var destination = await _egressPolicy.ValidateAsync(settings.Host, settings.Port, settings.EnableSsl, token);
            using var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient { Timeout = (int)_egressPolicy.Timeout.TotalMilliseconds };
            // Connect only to validated IPs; MailKit retains the original host for SNI/certificate validation.
            using var socket = await ConnectAsync(destination, token);
            await client.ConnectAsync(socket, destination.Host, destination.Port, destination.Security, token);
            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password, token);
            }
            await client.SendAsync(message, token);
            await client.DisconnectAsync(true, token);
        }
        catch (Exception) when (request.IsCancellationRequested)
        {
            // MailKit can wrap TLS cancellation in SslHandshakeException.
            throw new OperationCanceledException(request.Token);
        }
        catch (Exception exception) when (deadline.IsCancellationRequested || exception is TimeoutException)
        {
            throw new InvalidOperationException("SMTP 寄送逾時，請確認服務狀態。");
        }
        catch (Exception exception) when (exception is IOException or SocketException
            or MailKit.Net.Smtp.SmtpCommandException or MailKit.Security.AuthenticationException
            or MailKit.Security.SslHandshakeException or NotSupportedException)
        {
            // Do not expose server replies or credentials through UI errors or logs.
            throw new InvalidOperationException("SMTP 寄送失敗，請確認目的地、TLS 憑證與登入設定。");
        }
    }

    private static async Task<Socket> ConnectAsync(SmtpDestination destination, CancellationToken token)
    {
        foreach (var address in destination.Addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, destination.Port, token);
                return socket;
            }
            catch (SocketException) when (!token.IsCancellationRequested)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw new IOException("SMTP 連線失敗。");
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
