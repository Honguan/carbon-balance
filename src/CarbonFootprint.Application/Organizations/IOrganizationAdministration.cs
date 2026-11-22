using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Organizations;

namespace CarbonFootprint.Application.Organizations;

public sealed record MailSettingsInput(
    string? Host, int Port, bool EnableSsl, string? Username, string? Password, string? FromAddress, string? FromName);

/// <summary>Membership, invitation and SMTP settings operations of the current organization.</summary>
public interface IOrganizationAdministration
{
    /// <param name="acceptLink">Builds the absolute acceptance URL for an invitation token; the web layer owns routing.</param>
    Task<OperationResult> InviteMemberAsync(
        OperationContext context, string? email, OrganizationRole role, Func<string, string> acceptLink, CancellationToken cancellationToken);

    Task<OperationResult> RevokeInvitationAsync(OperationContext context, Guid invitationId, uint rowVersion, CancellationToken cancellationToken);

    Task<OperationResult> RevokeMemberAsync(OperationContext context, Guid membershipId, uint rowVersion, CancellationToken cancellationToken);

    Task<OperationResult> SaveMailSettingsAsync(OperationContext context, MailSettingsInput input, uint rowVersion, CancellationToken cancellationToken);

    Task<OperationResult> SendTestMailAsync(OperationContext context, string? recipient, CancellationToken cancellationToken);
}
