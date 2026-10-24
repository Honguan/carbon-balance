using System.Security.Claims;
using CarbonFootprint.Domain.Modules.Organizations;

namespace CarbonFootprint.Application.Operations;

/// <summary>Caller identity and tracing data an application operation needs, independent of any web framework type.</summary>
public sealed record OperationContext(ClaimsPrincipal User, Guid? ActorId, string CorrelationId, string? SessionReference);

public enum OperationStatus
{
    Succeeded,
    Forbidden,
    NotFound,
    /// <summary>The requested transition is not allowed from the record's current state.</summary>
    BadRequest,
    /// <summary>A business rule rejected the request; <see cref="OperationResult.Message"/> explains why.</summary>
    Invalid,
    /// <summary>The caller worked from a stale version; the rejection was audited and nothing changed.</summary>
    Conflict
}

public sealed record OperationResult(OperationStatus Status, string Key = "", string Message = "")
{
    public bool Succeeded => Status == OperationStatus.Succeeded;

    public static OperationResult Success(string message = "") => new(OperationStatus.Succeeded, Message: message);
    public static readonly OperationResult Forbidden = new(OperationStatus.Forbidden);
    public static readonly OperationResult NotFound = new(OperationStatus.NotFound);
    public static readonly OperationResult BadRequest = new(OperationStatus.BadRequest);
    public static readonly OperationResult Conflict = new(OperationStatus.Conflict);
    public static OperationResult Invalid(string key, string message) => new(OperationStatus.Invalid, key, message);
}

/// <summary>Authorization decision owned by the application boundary, so every caller (page, API, job) is checked the same way.</summary>
public interface IOperationAuthorizer
{
    Task<bool> IsAllowedAsync(OperationContext context, OrganizationPermission permission, bool requireMfa);
}
