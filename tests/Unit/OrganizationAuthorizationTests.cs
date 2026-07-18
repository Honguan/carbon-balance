using CarbonFootprint.Domain.Modules.Organizations;

namespace CarbonFootprint.Unit.Tests;

public sealed class OrganizationAuthorizationTests
{
    [Theory]
    [InlineData(OrganizationRole.Owner, OrganizationPermission.ManageOrganization, true)]
    [InlineData(OrganizationRole.Administrator, OrganizationPermission.ManageOrganization, true)]
    [InlineData(OrganizationRole.Contributor, OrganizationPermission.ManageOrganization, false)]
    [InlineData(OrganizationRole.Contributor, OrganizationPermission.EditInventory, true)]
    [InlineData(OrganizationRole.Reviewer, OrganizationPermission.EditInventory, false)]
    [InlineData(OrganizationRole.Reviewer, OrganizationPermission.CreateCalculationRun, true)]
    [InlineData(OrganizationRole.Viewer, OrganizationPermission.CreateCalculationRun, false)]
    [InlineData(OrganizationRole.Viewer, OrganizationPermission.ViewInventory, true)]
    public void PermissionMatrix_IsExplicit(
        OrganizationRole role,
        OrganizationPermission permission,
        bool expected)
    {
        Assert.Equal(expected, OrganizationPermissions.IsAllowed(role, permission));
    }
}
