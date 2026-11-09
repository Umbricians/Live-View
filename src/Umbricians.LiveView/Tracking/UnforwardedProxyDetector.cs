using System.Net;
using Microsoft.AspNetCore.Http;

namespace Umbricians.LiveView.Tracking;

/// <summary>
/// Remembers the last request that came through a proxy whose forwarded headers were not applied: one carrying
/// X-Forwarded-For from a private or loopback address. ASP.NET Core's forwarded headers middleware removes the header
/// once it has used it, so one left in place means visitors appear to come from the proxy.
/// </summary>
internal sealed class UnforwardedProxyDetector
{
    private const string ForwardedForHeader = "X-Forwarded-For";

    private static readonly IPNetwork[] PrivateNetworks =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
    ];

    private volatile IPAddress? _proxyAddress;

    /// <summary>The address of the proxy seen since the site started; null if none.</summary>
    public IPAddress? ProxyAddress => _proxyAddress;

    public void Observe(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey(ForwardedForHeader)
            || context.Connection.RemoteIpAddress is not { } address)
        {
            return;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || PrivateNetworks.Any(n => n.Contains(address)))
        {
            _proxyAddress = address;
        }
    }
}
