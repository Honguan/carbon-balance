namespace CarbonFootprint.Infrastructure.Identity;

public sealed class MailOptions
{
    public const string SectionName = "Mail";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool EnableSsl { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = "no-reply@carbon-footprint.local";

    public string FromName { get; set; } = "產品碳足跡系統";
}
