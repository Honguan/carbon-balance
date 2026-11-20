using CarbonFootprint.Application.Operations;
using CarbonFootprint.Application.Products;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class ProductCatalogueService(CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IOrganizationScope scope) : IProductCatalogue
{
    private Guid OrganizationId => scope.OrganizationId ?? throw new InvalidOperationException("缺少目前組織範圍。");

    public async Task<OperationResult> CreateFacilityAsync(OperationContext context, string? code, string? name, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageOrganization, requireMfa: false)) return OperationResult.Forbidden;
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) return OperationResult.Invalid("facility", "廠場代碼與名稱皆為必填。");

        var normalizedCode = code.Trim().ToUpperInvariant();
        if (await db.Facilities.AnyAsync(item => item.Code == normalizedCode, cancellationToken))
        {
            return OperationResult.Invalid("facilityCode", "廠場代碼不可重複。");
        }

        var facilityId = Guid.NewGuid();
        db.Facilities.Add(new FacilityRecord
        {
            Id = facilityId,
            OrganizationId = OrganizationId,
            Code = normalizedCode,
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        OperationAudit.AppendTracked(db, context, OrganizationId, "facility.created", "Facility", facilityId);
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("廠場已建立。");
    }

    public async Task<OperationResult> CreateProductAsync(
        OperationContext context, string? name, string? categoryCode, Guid facilityId, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.EditInventory, requireMfa: false)) return OperationResult.Forbidden;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(categoryCode)) return OperationResult.Invalid("productName", "產品名稱不可為空。");
        if (facilityId == Guid.Empty) return OperationResult.Invalid("facilityId", "請先建立並選擇所屬廠場。");
        var organizationId = OrganizationId;
        if (!await db.Facilities.AnyAsync(item => item.Id == facilityId && item.OrganizationId == organizationId, cancellationToken))
        {
            return OperationResult.Invalid("facilityId", "所選廠場不存在或不屬於目前組織。");
        }

        var productId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        db.Products.Add(new ProductRecord
        {
            Id = productId,
            OrganizationId = organizationId,
            Name = name.Trim(),
            CategoryCode = categoryCode.Trim().ToUpperInvariant(),
            FacilityId = facilityId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.ProductVersions.Add(new ProductVersionRecord
        {
            Id = versionId,
            OrganizationId = organizationId,
            ProductId = productId,
            VersionNumber = 1,
            NameZhTw = name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        OperationAudit.AppendTracked(db, context, organizationId, "product.version.created", "ProductVersion", versionId);
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("產品與第 1 版已建立。");
    }
}
