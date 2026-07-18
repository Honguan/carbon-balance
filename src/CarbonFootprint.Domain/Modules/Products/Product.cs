namespace CarbonFootprint.Domain.Modules.Products;

public sealed class Product
{
    public Product(Guid id, Guid organizationId, string name)
    {
        if (id == Guid.Empty || organizationId == Guid.Empty)
        {
            throw new ArgumentException("產品與組織 ID 不可為空。", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("產品名稱不可為空。", nameof(name));
        }

        Id = id;
        OrganizationId = organizationId;
        Name = name.Trim();
    }

    private Product()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }
}

public sealed class ProductVersion
{
    public ProductVersion(Guid id, Guid organizationId, Guid productId, int versionNumber, string nameZhTw)
    {
        if (id == Guid.Empty || organizationId == Guid.Empty || productId == Guid.Empty)
        {
            throw new ArgumentException("產品版本識別不可為空。", nameof(id));
        }

        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber));
        }

        if (string.IsNullOrWhiteSpace(nameZhTw))
        {
            throw new ArgumentException("產品版本名稱不可為空。", nameof(nameZhTw));
        }

        Id = id;
        OrganizationId = organizationId;
        ProductId = productId;
        VersionNumber = versionNumber;
        NameZhTw = nameZhTw.Trim();
    }

    private ProductVersion()
    {
        NameZhTw = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid ProductId { get; private set; }

    public int VersionNumber { get; private set; }

    public string NameZhTw { get; private set; }
}

