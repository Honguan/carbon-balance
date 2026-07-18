namespace CarbonFootprint.Domain.Modules.Organizations;

public enum OrganizationRole
{
    Owner = 1,
    Administrator = 2,
    Contributor = 3,
    Reviewer = 4,
    Viewer = 5
}

public enum OrganizationPermission
{
    ManageOrganization = 1,
    EditInventory = 2,
    ManageFactors = 3,
    CreateCalculationRun = 4,
    ReviewInventory = 5,
    ViewInventory = 6
}

public static class OrganizationPermissions
{
    public static bool IsAllowed(OrganizationRole role, OrganizationPermission permission) => permission switch
    {
        OrganizationPermission.ManageOrganization => role is OrganizationRole.Owner or OrganizationRole.Administrator,
        OrganizationPermission.EditInventory => role is OrganizationRole.Owner or OrganizationRole.Administrator or OrganizationRole.Contributor,
        OrganizationPermission.ManageFactors => role is OrganizationRole.Owner or OrganizationRole.Administrator,
        OrganizationPermission.CreateCalculationRun => role is not OrganizationRole.Viewer,
        OrganizationPermission.ReviewInventory => role is OrganizationRole.Owner or OrganizationRole.Reviewer,
        OrganizationPermission.ViewInventory => true,
        _ => false
    };
}
