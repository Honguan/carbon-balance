using System.Security.Cryptography;
using System.Text;

namespace CarbonFootprint.Web.Security;

public static class EvidenceRequestHash
{
    public static string Create(string? value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "unknown")));
}
