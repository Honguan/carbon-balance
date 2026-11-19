using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Application.Inventories;

/// <summary>One activity data entry as submitted; "__other__" selections carry their free-text value in the matching *Other field.</summary>
public sealed record ActivityInput(
    Guid InventoryProjectVersionId,
    LifecycleStage LifecycleStage,
    ActivityDataKind ActivityKind,
    string? ActivityName,
    string? ActivityNameOther,
    string? SupplierOrScenario,
    string? EquipmentCategory,
    string? EquipmentCategoryOther,
    string? DataSourceType,
    string? DataSourceTypeOther,
    string? DataProviderType,
    string? DataProviderOther,
    string? CollectionMethod,
    string? CollectionMethodOther,
    string? SourceReference,
    decimal? RawValue,
    decimal? TransportDistanceKm,
    decimal? TransportWeightKg,
    decimal? UseLifetime,
    decimal? UseFrequency,
    decimal? UseConsumptionPerUse,
    string? RawUnitCode,
    string? CanonicalUnitCode,
    Guid FactorVersionId,
    decimal? AllocationFactor,
    bool IsEstimated,
    string? EstimationReason,
    string? DataQuality);

/// <summary>An uploaded evidence file; <see cref="Content"/> is null when nothing was chosen.</summary>
public sealed record EvidenceUpload(Stream? Content, long Length, string FileName, string ContentType);

/// <summary>Lifecycle activity data and its evidence. Corrections retire the old version and create a new one; nothing is overwritten.</summary>
public interface IActivityData
{
    Task<OperationResult> SaveAsync(OperationContext context, ActivityInput input, Guid? supersedesActivityId, CancellationToken cancellationToken);

    Task<OperationResult> RetireAsync(OperationContext context, Guid activityId, CancellationToken cancellationToken);

    Task<OperationResult> AttachEvidenceAsync(OperationContext context, Guid activityId, EvidenceUpload upload, CancellationToken cancellationToken);
}
