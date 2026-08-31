using System.Data.Common;

namespace CarbonFootprint.Web.Security;

public static class DeploymentSecurity
{
    public static void Validate(IConfiguration configuration, string environmentName)
    {
        var isDevelopment = string.Equals(
            environmentName,
            "Development",
            StringComparison.OrdinalIgnoreCase);
        if (!isDevelopment)
        {
            Require(configuration.GetValue<bool>("Security:RequireHttpsCookies"),
                "非 Development 環境必須設定 Security:RequireHttpsCookies=true。");
        }

        if (!configuration.GetValue<bool>("Deployment:Hardened"))
        {
            return;
        }

        if (isDevelopment)
        {
            throw new InvalidOperationException("Hardened deployment 不得使用 Development 環境。");
        }

        Require(!string.IsNullOrWhiteSpace(configuration["Deployment:SecretProvider"]),
            "Hardened deployment 必須定義 Deployment:SecretProvider。");
        Require(configuration.GetValue<int?>("https_port") is > 0,
            "Hardened deployment 必須定義有效的 HTTPS port。");

        var connectionString = configuration.GetConnectionString("Database") ?? string.Empty;
        var connection = new DbConnectionStringBuilder { ConnectionString = connectionString };
        Require(connection.TryGetValue("SSL Mode", out var sslMode)
                && string.Equals(sslMode?.ToString(), "VerifyFull", StringComparison.OrdinalIgnoreCase),
            "Hardened deployment 的 PostgreSQL 連線必須使用 SSL Mode=VerifyFull。");

        Require(Uri.TryCreate(configuration["ObjectStorage:Endpoint"], UriKind.Absolute, out var endpoint)
                && endpoint.Scheme == Uri.UriSchemeHttps,
            "Hardened deployment 的 ObjectStorage:Endpoint 必須使用 HTTPS。");
        Require(configuration.GetValue<bool>("Mail:EnableSsl"),
            "Hardened deployment 必須設定 Mail:EnableSsl=true。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
