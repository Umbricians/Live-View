using System.Net;
using Microsoft.AspNetCore.Http;
using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.HealthChecks;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>Whether requests that carry X-Forwarded-For from a proxy are noticed.</summary>
public class ForwardedHeadersCheckTests
{
    private UnforwardedProxyDetector _detector;

    [SetUp]
    public void SetUp() => _detector = new UnforwardedProxyDetector();

    [TestCase("10.0.0.4", "10.0.0.4")]
    [TestCase("172.20.1.1", "172.20.1.1")]
    [TestCase("192.168.1.10", "192.168.1.10")]
    [TestCase("127.0.0.1", "127.0.0.1")]
    [TestCase("::1", "::1")]
    [TestCase("fd00::1", "fd00::1")]
    [TestCase("::ffff:10.0.0.4", "10.0.0.4")]
    public async Task A_forwarded_request_from_a_private_or_loopback_address_is_a_warning(string remote, string shown)
    {
        _detector.Observe(Request(remote, forwarded: true));

        HealthCheckStatus status = await Run();

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Warning));
        Assert.That(status.Message, Does.Contain(shown));
        Assert.That(status.Message, Does.Contain("heuristic"));
        Assert.That(status.ReadMoreLink, Is.Not.Null);
    }

    [Test]
    public async Task A_forwarded_request_from_a_public_address_is_fine()
    {
        _detector.Observe(Request("203.0.113.7", forwarded: true));

        Assert.That((await Run()).ResultType, Is.EqualTo(StatusResultType.Success));
    }

    [Test]
    public async Task A_private_address_without_a_forwarded_header_is_fine()
    {
        _detector.Observe(Request("10.0.0.4", forwarded: false));

        Assert.That((await Run()).ResultType, Is.EqualTo(StatusResultType.Success));
    }

    [Test]
    public async Task With_no_requests_the_check_passes_and_says_it_is_a_heuristic()
    {
        HealthCheckStatus status = await Run();

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Success));
        Assert.That(status.Message, Does.Contain("heuristic"));
    }

    private async Task<HealthCheckStatus> Run()
    {
        IEnumerable<HealthCheckStatus> statuses = await new ForwardedHeadersCheck(_detector).GetStatusAsync();
        Assert.That(statuses, Has.Exactly(1).Items);
        return statuses.Single();
    }

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
