using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

/// <summary>Requests through the tracking middleware, whether or not they are counted.</summary>
public class LiveActivityMiddlewareTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly UnforwardedProxyDetector _detector = new();

    [Theory]
    [InlineData(false, "GET")]
    [InlineData(true, "HEAD")]
    public async Task A_forwarded_request_from_a_proxy_is_noticed_even_when_it_is_not_counted(bool trackingEnabled, string method)
    {
        LiveActivityMiddleware middleware = BuildMiddleware(trackingEnabled);
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = "/";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.4");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
        bool nextRan = false;

        await middleware.InvokeAsync(context, _ =>
        {
            nextRan = true;
            return Task.CompletedTask;
        });

        Assert.True(nextRan);
        Assert.Equal(IPAddress.Parse("10.0.0.4"), _detector.ProxyAddress);
    }

    private LiveActivityMiddleware BuildMiddleware(bool trackingEnabled)
    {
        var options = new StaticOptionsMonitor<LiveViewOptions>(new LiveViewOptions { TrackingEnabled = trackingEnabled });
        var users = new InMemoryBackofficeUserDirectory();
        var members = new InMemoryMemberDirectory();
        var backoffice = new BackofficePresence(users, options, _clock);
        var memberPresence = new MemberPresence(members, options, _clock);
        var callers = new McpCallers(backoffice, memberPresence, users, members, _clock, NullLogger<McpCallers>.Instance);
        return new LiveActivityMiddleware(
            new RequestClassifier(options), new LiveActivityTracker(options, _clock), backoffice, memberPresence, callers, _detector);
    }
}
