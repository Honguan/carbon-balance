using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Inventories;
using CarbonFootprint.Application.Organizations;
using CarbonFootprint.Application.Products;
using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Authorization;

namespace CarbonFootprint.Integration.Tests;

/// <summary>Real inventory services over the test database, so page-level tests exercise the same code as production.</summary>
internal static class InventoryServices
{
    public static IInventoryReadiness Readiness(CarbonFootprintDbContext db) =>
        new InventoryReadinessService(db, new InventorySnapshotReader(db));

    public static IInventoryWorkflow Workflow(CarbonFootprintDbContext db, IAuthorizationService authorization, IOrganizationScope scope) =>
        new InventoryWorkflowService(db, new OperationAuthorizer(authorization), scope, Readiness(db));

    // Evidence storage is left out: page-level tests that upload evidence build their own service.
    public static IActivityData Activities(CarbonFootprintDbContext db, IAuthorizationService authorization, IOrganizationScope scope) =>
        new ActivityDataService(db, new OperationAuthorizer(authorization), scope, null!);

    // Invitations are not exercised through page-level tests; pass a real sender only where SMTP handlers run.
    public static IOrganizationAdministration Organization(CarbonFootprintDbContext db, IAuthorizationService authorization,
        IOrganizationScope scope, SmtpEmailSender emailSender, UserManager<ApplicationUser> userManager) =>
        new OrganizationAdministrationService(db, new OperationAuthorizer(authorization), scope, null!, emailSender, userManager,
            new EphemeralDataProtectionProvider());

    public static IProductCatalogue Products(CarbonFootprintDbContext db, IAuthorizationService authorization, IOrganizationScope scope) =>
        new ProductCatalogueService(db, new OperationAuthorizer(authorization), scope);
}
