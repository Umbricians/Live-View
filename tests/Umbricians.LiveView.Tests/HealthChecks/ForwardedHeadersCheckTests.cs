using System.Net;
using Microsoft.AspNetCore.Http;
using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.HealthChecks;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>Whether requests that carry X-Forwarded-For from a proxy are noticed.</summary>
public class ForwardedHeadersCheckTests
{
    private readonly UnforwardedProxyDetector _detector = new();

    [Theory]
    [InlineData("10.0.0.4", "10.0.0.4")]
    [InlineData("172.20.1.1", "172.20.1.1")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("::1", "::1")]
    [InlineData("fd00::1", "fd00::1")]
    [InlineData("::ffff:10.0.0.4", "10.0.0.4")]
    public async Task A_forwarded_request_from_a_private_or_loopback_address_is_a_warning(string remote, string shown)
    {
        _detector.Observe(Request(remote, forwarded: true));

        HealthCheckStatus status = await Run();

        Assert.Equal(StatusResultType.Warning, status.ResultType);
        Assert.Contains(shown, status.Message);
        Assert.Contains("heuristic", status.Message);
        Assert.NotNull(status.ReadMoreLink);
    }

    [Fact]
    public async Task A_forwarded_request_from_a_public_address_is_fine()
    {
        _detector.Observe(Request("203.0.113.7", forwarded: true));

        Assert.Equal(StatusResultType.Success, (await Run()).ResultType);
    }

    [Fact]
    public async Task A_private_address_without_a_forwarded_header_is_fine()
    {
        _detector.Observe(Request("10.0.0.4", forwarded: false));

        Assert.Equal(StatusResultType.Success, (await Run()).ResultType);
    }

    [Fact]
    public async Task With_no_requests_the_check_passes_and_says_it_is_a_heuristic()
    {
        HealthCheckStatus status = await Run();

        Assert.Equal(StatusResultType.Success, status.ResultType);
        Assert.Contains("heuristic", status.Message);
    }

    private async Task<HealthCheckStatus> Run() =>
        Assert.Single(await new ForwardedHeadersCheck(_detector).GetStatusAsync());

    private static DefaultHttpContext Request(string remote, bool forwarded)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (forwarded)
        {
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
        }

        return context;
    }
}
