using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

/// <summary>
/// Session-key sources are written by hand in configuration, so they match however they are cased, and the same
/// session keys the same way whatever the casing.
/// </summary>
public class SessionKeySourceTests
{
    private const string Channel = "test";

    private readonly RecordingTracker _tracker = new();
    private readonly LiveViewOptions _options = new();
    private readonly LiveActivityMiddleware _middleware;

    public SessionKeySourceTests()
    {
        var clock = new FakeTimeProvider();
        var options = new StaticOptionsMonitor<LiveViewOptions>(_options);
        var users = new EmptyBackofficeUserDirectory();
        var members = new EmptyMemberDirectory();
        var backofficePresence = new BackofficePresence(users, options, clock);
        var memberPresence = new MemberPresence(members, options, clock);
        _middleware = new LiveActivityMiddleware(
            _tracker,
            backofficePresence,
            memberPresence,
            new McpCallers(backofficePresence, memberPresence, users, members, clock, NullLogger<McpCallers>.Instance),
            options);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("user")]
    [InlineData("USER")]
    public async Task The_user_source_matches_any_casing(string source)
    {
        Guid userKey = Guid.NewGuid();
        DefaultHttpContext context = Request(source);

        await _middleware.InvokeAsync(context, c =>
        {
            // Authentication runs further down the pipeline, so the user is only known once it has.
            c.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userKey.ToString())], "Test"));
            return Task.CompletedTask;
        });

        Assert.Equal([$"User:{userKey}"], _tracker.Touched);
        Assert.Empty(_tracker.Begun);
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("client")]
    public async Task The_client_source_matches_any_casing(string source)
    {
        DefaultHttpContext context = Request(source);
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        context.Request.Headers.UserAgent = "Browser/1.0";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(["Client:203.0.113.7|Browser/1.0"], _tracker.Begun);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    public async Task The_authorization_source_matches_any_casing(string source)
    {
        DefaultHttpContext context = Request(source);
        context.Request.Headers.Authorization = "Bearer abc";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(["Authorization:Bearer abc"], _tracker.Begun);
    }

    [Theory]
    [InlineData("Header:Mcp-Session-Id")]
    [InlineData("header:Mcp-Session-Id")]
    public async Task The_header_prefix_matches_any_casing(string source)
    {
        DefaultHttpContext context = Request(source);
        context.Request.Headers["Mcp-Session-Id"] = "s1";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(["Header:Mcp-Session-Id:s1"], _tracker.Begun);
    }

    [Theory]
    [InlineData("Cookie:_ga")]
    [InlineData("COOKIE:_ga")]
    public async Task The_cookie_prefix_matches_any_casing(string source)
    {
        DefaultHttpContext context = Request(source);
        context.Request.Headers.Cookie = "_ga=g1";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(["Cookie:_ga:g1"], _tracker.Begun);
    }

    /// <summary>A GET for <c>/test</c>, tracked by a single channel keyed on <paramref name="source"/>.</summary>
    private DefaultHttpContext Request(string source)
    {
        _options.Channels[Channel] = new LiveViewChannelOptions
        {
            PathPrefixes = ["/test"],
            SessionKeys = [source],
        };

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/test";
        return context;
    }

    /// <summary>Records the session keys the middleware hands the tracker for the test channel.</summary>
    private sealed class RecordingTracker : ILiveActivityTracker
    {
        public List<string> Touched { get; } = [];

        public List<string> Begun { get; } = [];

        public void Touch(string channel, string sessionKey) => Record(Touched, channel, sessionKey);

        public IDisposable BeginActivity(string channel, string sessionKey)
        {
            Record(Begun, channel, sessionKey);
            return new NoOpDisposable();
        }

        public void End(string channel, string sessionKey)
        {
        }

        public int CountActive(string channel) => 0;

        private static void Record(List<string> keys, string channel, string sessionKey)
        {
            if (channel == Channel)
            {
                keys.Add(sessionKey);
            }
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class EmptyMemberDirectory : IMemberDirectory
    {
        public Guid? FindKey(int memberId) => null;

        public Task<IReadOnlyList<MemberDetails>> GetAsync(IReadOnlyCollection<Guid> keys) =>
            Task.FromResult<IReadOnlyList<MemberDetails>>([]);
    }

    private sealed class EmptyBackofficeUserDirectory : IBackofficeUserDirectory
    {
        public Task<IReadOnlyList<BackofficeUserDetails>> GetAsync(IReadOnlyCollection<Guid> keys) =>
            Task.FromResult<IReadOnlyList<BackofficeUserDetails>>([]);
    }
}
