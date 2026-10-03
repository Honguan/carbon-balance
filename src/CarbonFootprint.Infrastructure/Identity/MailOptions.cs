namespace CarbonFootprint.Infrastructure.Identity;

public sealed class MailOptions
{
    public const string SectionName = "Mail";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool EnableSsl { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public List<SmtpRelayOptions> TrustedRelays { get; set; } = [];

    public SmtpRelayOptions DevelopmentMailpit { get; set; } = new()
    {
        Host = "localhost",
        Port = 1025,
        AddressRanges = ["127.0.0.1/32", "::1/128"],
        AllowInsecure = true
    };

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = "no-reply@carbon-footprint.local";

    public string FromName { get; set; } = "產品碳足跡系統";
}

public sealed class SmtpRelayOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string[] AddressRanges { get; set; } = [];

    public bool AllowInsecure { get; set; }
}
