namespace CarbonFootprint.Domain.Modules.Organizations;

public sealed class Organization
{
    public Organization(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("組織 ID 不可為空。", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("組織名稱不可為空。", nameof(name));
        }

        Id = id;
        Name = name.Trim();
    }

    private Organization()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }
}

