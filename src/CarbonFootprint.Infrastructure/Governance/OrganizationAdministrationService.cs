using CarbonFootprint.Application.Operations;
using CarbonFootprint.Application.Organizations;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class OrganizationAdministrationService(
    CarbonFootprintDbContext db,
    IOperationAuthorizer authorizer,
    IOrganizationScope scope,
    OrganizationInvitationService invitations,
    SmtpEmailSender emailSender,
    UserManager<ApplicationUser> userManager,
    IDataProtectionProvider dataProtectionProvider) : IOrganizationAdministration
{
    private const string MailResource = "OrganizationMailSettings";
    // Must match SmtpEmailSender, which decrypts the stored password with the same purpose.
    private readonly IDataProtector _passwordProtector = dataProtectionProvider.CreateProtector("CarbonFootprint.OrganizationMailSettings.v1");
    private Guid OrganizationId => scope.OrganizationId ?? throw new InvalidOperationException("缺少目前組織範圍。");

    public async Task<OperationResult> InviteMemberAsync(
        OperationContext context, string? email, OrganizationRole role, Func<string, string> acceptLink, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageOrganization, requireMfa: true)) return OperationResult.Forbidden;
        if (context.ActorId is not { } invitedBy) return OperationResult.Forbidden;
        try
        {
            var token = await invitations.CreateAsync(OrganizationId, invitedBy, email!, role, cancellationToken,
                context.CorrelationId, context.SessionReference);
            await emailSender.SendOrganizationInvitationAsync(email!.Trim(), acceptLink(token), cancellationToken);
            return OperationResult.Success("組織邀請已寄出。");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return OperationResult.Invalid("invitation", exception.Message);
        }
    }

    public async Task<OperationResult> RevokeInvitationAsync(OperationContext context, Guid invitationId, uint rowVersion, CancellationToken cancellationToken)
    {
        const string resource = "OrganizationInvitation";
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageOrganization, requireMfa: true)) return OperationResult.Forbidden;
        var invitation = await db.OrganizationInvitations.SingleOrDefaultAsync(item => item.Id == invitationId, cancellationToken);
        if (invitation is null) return OperationResult.NotFound;
        if (rowVersion != invitation.RowVersion) return await StaleAsync(context, "RevokeInvitation", resource, invitation.Id, cancellationToken);
        // Accepted invitations are already memberships; revoking them is a no-op here.
        if (invitation.AcceptedAt.HasValue) return OperationResult.Success();

        invitation.RevokedAt = DateTimeOffset.UtcNow;
        OperationAudit.AppendTracked(db, context, OrganizationId, "organization.invitation.revoked", resource, invitation.Id);
        return await SaveAsync(context, "RevokeInvitation", resource, invitation.Id, OperationResult.Success(), cancellationToken);
    }

    public async Task<OperationResult> RevokeMemberAsync(OperationContext context, Guid membershipId, uint rowVersion, CancellationToken cancellationToken)
    {
        const string resource = "OrganizationMembership";
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageOrganization, requireMfa: true)) return OperationResult.Forbidden;
        var membership = await db.OrganizationMemberships.SingleOrDefaultAsync(item => item.Id == membershipId, cancellationToken);
        if (membership is null) return OperationResult.NotFound;
        if (rowVersion != membership.RowVersion) return await StaleAsync(context, "RevokeMember", resource, membership.Id, cancellationToken);
        if (membership.Role == OrganizationRole.Owner.ToString()) return OperationResult.Invalid("membership", "不可撤銷組織擁有者。");

        var revokedUser = await userManager.FindByIdAsync(membership.UserId.ToString());
        if (revokedUser is null) return OperationResult.NotFound;
        // Rotating the stamp signs the revoked member out of existing sessions.
        if (!(await userManager.UpdateSecurityStampAsync(revokedUser)).Succeeded)
        {
            return OperationResult.Invalid("membership", "無法更新成員登入安全狀態，請稍後再試。");
        }

        membership.RevokedAt = DateTimeOffset.UtcNow;
        OperationAudit.AppendTracked(db, context, OrganizationId, "organization.membership.revoked", resource, membership.Id);
        return await SaveAsync(context, "RevokeMember", resource, membership.Id, OperationResult.Success(), cancellationToken);
    }

    public async Task<OperationResult> SaveMailSettingsAsync(
        OperationContext context, MailSettingsInput input, uint rowVersion, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageOrganization, requireMfa: true)) return OperationResult.Forbidden;
        var host = input.Host?.Trim() ?? string.Empty;
        var username = input.Username?.Trim() ?? string.Empty;
        var fromAddress = input.FromAddress?.Trim() ?? string.Empty;
        var fromName = input.FromName?.Trim() ?? string.Empty;
        var password = input.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host) || host.Length > 300
            || input.Port is < 1 or > 65535
            || !IsExactMailAddress(fromAddress) || fromAddress.Length > 320
            || string.IsNullOrWhiteSpace(fromName) || fromName.Length > 200
            || username.Length > 320
            || password.Length > 1000)
        {
            return OperationResult.Invalid("mail", "請填寫有效的 SMTP 主機、連接埠、寄件地址與寄件人名稱。");
        }

        try
        {
            await emailSender.ValidateSettingsAsync(host, input.Port, input.EnableSsl, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return OperationResult.Invalid("mail", exception.Message);
        }

        var settings = await db.OrganizationMailSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new OrganizationMailSettingsRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = OrganizationId,
                Host = host,
                Port = input.Port,
                EnableSsl = input.EnableSsl,
                Username = username,
                EncryptedPassword = string.IsNullOrWhiteSpace(password) ? string.Empty : _passwordProtector.Protect(password),
                FromAddress = fromAddress,
                FromName = fromName,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = context.ActorId
            };
            db.OrganizationMailSettings.Add(settings);
        }
        else
        {
            if (rowVersion != settings.RowVersion) return await StaleAsync(context, "SaveMailSettings", MailResource, settings.Id, cancellationToken);
            settings.Host = host;
            settings.Port = input.Port;
            settings.EnableSsl = input.EnableSsl;
            settings.Username = username;
            // A blank password keeps the stored credential.
            if (!string.IsNullOrWhiteSpace(password)) settings.EncryptedPassword = _passwordProtector.Protect(password);
            settings.FromAddress = fromAddress;
            settings.FromName = fromName;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            settings.UpdatedBy = context.ActorId;
        }

        OperationAudit.AppendTracked(db, context, OrganizationId, "organization.mail_settings.updated", MailResource, settings.Id,
            metadata: new Dictionary<string, object?> { ["credentialsChanged"] = !string.IsNullOrWhiteSpace(password) });
        return await SaveAsync(context, "SaveMailSettings", MailResource, settings.Id,
            OperationResult.Success("SMTP 設定已儲存。密碼以資料保護機制加密保存。"), cancellationToken);
    }

    public async Task<OperationResult> SendTestMailAsync(OperationContext context, string? recipient, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageOrganization, requireMfa: true)) return OperationResult.Forbidden;
        var normalized = recipient?.Trim() ?? string.Empty;
        if (!IsExactMailAddress(normalized) || normalized.Length > 320) return OperationResult.Invalid("recipient", "請輸入測試收件地址。");

        try
        {
            await emailSender.SendTestMessageAsync(normalized, cancellationToken);
            var mailSettingsId = await db.OrganizationMailSettings.AsNoTracking().Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken);
            new AuditWriter(db).Append("organization.mail_settings.tested",
                mailSettingsId.HasValue ? MailResource : "Organization", mailSettingsId ?? OrganizationId, OrganizationId, context.ActorId,
                null, null, "SMTP test; no resource state mutation", context.CorrelationId, context.SessionReference, "attempt");
            await db.SaveChangesAsync(cancellationToken);
            return OperationResult.Success("測試信已送出，請確認收件匣或 SMTP 服務紀錄。");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Net.Mail.SmtpException or ArgumentException
                                              or System.Security.Cryptography.CryptographicException)
        {
            return OperationResult.Invalid("mail", $"SMTP 測試失敗：{exception.Message}");
        }
    }

    private static bool IsExactMailAddress(string value)
    {
        try
        {
            return new System.Net.Mail.MailAddress(value).Address.Equals(value, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private async Task<OperationResult> StaleAsync(
        OperationContext context, string operation, string resourceType, Guid id, CancellationToken cancellationToken)
    {
        await OperationAudit.RecordStaleRejectionAsync(db, context, OrganizationId, operation, resourceType, id, cancellationToken);
        return OperationResult.Conflict;
    }

    private async Task<OperationResult> SaveAsync(
        OperationContext context, string operation, string resourceType, Guid id, OperationResult success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await StaleAsync(context, operation, resourceType, id, cancellationToken);
        }
    }
}
