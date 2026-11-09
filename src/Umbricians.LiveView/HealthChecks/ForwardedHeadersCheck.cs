using System.Net;
using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.HealthChecks;

/// <summary>Warns when requests have arrived from a proxy whose forwarded headers were not applied.</summary>
[HealthCheck(
    "6b1f0d3e-8a52-4c7e-9f14-2d6a7c3e5b81",
    "Visitor IP addresses behind a proxy",
    Description = "Behind a proxy or load balancer, web sessions can only be told apart if ASP.NET Core's forwarded headers pass on each visitor's own IP address. A heuristic based on the requests since the site started.",
    Group = LiveViewHealthChecks.Group)]
internal sealed class ForwardedHeadersCheck : HealthCheck
{
    private readonly UnforwardedProxyDetector _detector;

    public ForwardedHeadersCheck(UnforwardedProxyDetector detector) => _detector = detector;

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        HealthCheckStatus status = _detector.ProxyAddress is { } address
            ? new HealthCheckStatus(
                $"Requests are arriving with an <code>X-Forwarded-For</code> header from <strong>{WebUtility.HtmlEncode(address.ToString())}</strong>, "
                + "a private or loopback address, so the site takes that proxy's address as every visitor's and visitors with the same browser are counted as one web session. "
                + "Configure ASP.NET Core's forwarded headers and trust this proxy (<code>KnownProxies</code> or <code>KnownIPNetworks</code>) so each visitor's own address is used. "
                + "This is a heuristic based on requests since the site started.")
            {
                ResultType = StatusResultType.Warning,
                ReadMoreLink = LiveViewHealthChecks.ForwardedHeadersLink,
            }
            : new HealthCheckStatus(
                "No request since the site started has come through a proxy without forwarded headers. "
                + "This is a heuristic: it can only judge requests that carry an <code>X-Forwarded-For</code> header.")
            {
                ResultType = StatusResultType.Success,
            };

        return Task.FromResult<IEnumerable<HealthCheckStatus>>([status]);
    }
}
