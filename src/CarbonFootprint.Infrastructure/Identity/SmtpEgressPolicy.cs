using System.Net;
using System.Net.Sockets;
using MailKit.Security;

namespace CarbonFootprint.Infrastructure.Identity;

public sealed class SmtpEgressPolicy
{
    private static readonly IPNetwork[] BlockedNetworks =
    [
        IPNetwork.Parse("0.0.0.0/8"), IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"), IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"), IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"), IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"), IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"), IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/3"), IPNetwork.Parse("168.63.129.16/32"),
        IPNetwork.Parse("2001::/23"), IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"), IPNetwork.Parse("3fff::/20")
    ];
    private static readonly IPNetwork GlobalIpv6 = IPNetwork.Parse("2000::/3");
    private readonly MailOptions _options;
    private readonly bool _isDevelopment;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public SmtpEgressPolicy(
        MailOptions options,
        bool isDevelopment,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        if (options.TimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException("Mail:TimeoutSeconds 必須介於 1 與 120 秒。");
        }
        _options = options;
        _isDevelopment = isDevelopment;
        _resolve = resolve ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
    }

    public TimeSpan Timeout => TimeSpan.FromSeconds(_options.TimeoutSeconds);

    public async Task<SmtpDestination> ValidateAsync(
        string host, int port, bool enableSsl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("SMTP 主機或連接埠無效。");
        }
        var relays = _options.TrustedRelays.Where(relay => MatchesEndpoint(relay, host, port)).ToList();
        if (_isDevelopment && MatchesEndpoint(_options.DevelopmentMailpit, host, port))
        {
            relays.Add(_options.DevelopmentMailpit);
        }
        if (!enableSsl && !relays.Any(relay => relay.AllowInsecure))
        {
            throw new InvalidOperationException("SMTP 必須啟用 TLS；只有伺服器設定的受信任 relay 或開發 Mailpit 可例外。");
        }

        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await _resolve(host, cancellationToken).WaitAsync(cancellationToken);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException("SMTP 主機未解析到 IP 位址。");
        }
        addresses = addresses.Select(Normalize).Distinct().ToArray();
        foreach (var address in addresses)
        {
            var allowedRelay = relays.Any(relay =>
                (enableSsl || relay.AllowInsecure) && relay.AddressRanges.Any(range => Contains(range, address)));
            if (!IsUsable(address) || (!IsPublic(address) && !allowedRelay) || (!enableSsl && !allowedRelay))
            {
                throw new InvalidOperationException("SMTP 目的地遭輸出網路政策拒絕；內部位址須由伺服器管理員明確核准主機、連接埠與 IP/CIDR。");
            }
        }
        return new SmtpDestination(host, port, addresses,
            enableSsl ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) : SecureSocketOptions.None);
    }

    private static bool MatchesEndpoint(SmtpRelayOptions relay, string host, int port) =>
        relay.Port == port && relay.Host.TrimEnd('.').Equals(host.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool Contains(string range, IPAddress address) => range.Contains('/')
        ? IPNetwork.TryParse(range, out var network) && network.Contains(address)
        : IPAddress.TryParse(range, out var literal) && Normalize(literal).Equals(address);

    private static bool IsUsable(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork
            ? !IPAddress.Any.Equals(address) && address.GetAddressBytes()[0] < 224
            : address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId == 0
                && !IPAddress.IPv6Any.Equals(address) && !address.IsIPv6Multicast;

    private static bool IsPublic(IPAddress address) =>
        (address.AddressFamily == AddressFamily.InterNetwork || GlobalIpv6.Contains(address))
        && !BlockedNetworks.Any(network => network.Contains(address));
}

public sealed record SmtpDestination(string Host, int Port, IPAddress[] Addresses, SecureSocketOptions Security);
