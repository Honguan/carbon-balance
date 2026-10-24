using CarbonFootprint.Application.Governance;
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
}
