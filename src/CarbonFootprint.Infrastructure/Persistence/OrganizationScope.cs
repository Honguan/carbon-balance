namespace CarbonFootprint.Infrastructure.Persistence;

public interface IOrganizationScope
{
    Guid? OrganizationId { get; }
}

public sealed class UnscopedOrganizationScope : IOrganizationScope
{
    public Guid? OrganizationId => null;
}

