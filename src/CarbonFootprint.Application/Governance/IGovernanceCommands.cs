using CarbonFootprint.Application.Operations;

namespace CarbonFootprint.Application.Governance;

/// <summary>PCR version review and publication transitions. Every call authorizes, validates, audits and saves atomically.</summary>
public interface IPcrGovernance
{
    Task<OperationResult> ReviewAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> RejectAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> PublishAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> WithdrawAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken);
}

/// <summary>Emission factor version review and publication transitions.</summary>
public interface IFactorGovernance
{
    Task<OperationResult> ReviewAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> PublishAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> WithdrawAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken);
}
