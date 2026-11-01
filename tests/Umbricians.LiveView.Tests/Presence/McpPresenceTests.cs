using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Presence;

/// <summary>People using MCP, through the tracking middleware and the push interface, as the tables see them.</summary>
public class McpPresenceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryBackofficeUserDirectory _users = new();
    private readonly InMemoryMemberDirectory _members = new();
    private readonly BackofficePresence _backoffice;
    private readonly MemberPresence _memberPresence;
    private readonly McpCallers _callers;
    private readonly LiveActivityMiddleware _middleware;

    public McpPresenceTests()
    {
        var liveViewOptions = new LiveViewOptions();
        liveViewOptions.Channels[Constants.Channels.Mcp] = new LiveViewChannelOptions
        {
            PathPrefixes = ["/mcp", "/umbraco/mcp"],
            SessionKeys = [Constants.SessionKeySources.HeaderPrefix + "Mcp-Session-Id", Constants.SessionKeySources.Client],
        };
        var options = new StaticOptionsMonitor<LiveViewOptions>(liveViewOptions);
        _backoffice = new BackofficePresence(_users, options, _clock);
        _memberPresence = new MemberPresence(_members, options, _clock);
        _callers = new McpCallers(_backoffice, _memberPresence, _users, _members, _clock, NullLogger<McpCallers>.Instance);
        _middleware = new LiveActivityMiddleware(new NoTracker(), _backoffice, _memberPresence, _callers, options);
    }

    [Fact]
    public async Task A_member_on_mcp_is_listed_as_on_mcp_only()
    {
        Guid alice = _members.Add(1, "Alice");

        await CallMcp(SignedInWithKey(alice));

        (int total, IReadOnlyList<OnlineMember> page) = await _memberPresence.PageAsync(0, 10);
        OnlineMember row = Assert.Single(page);
        Assert.Equal(1, total);
        Assert.Null(row.WebsiteLastSeen);
        Assert.Equal(_clock.GetUtcNow(), row.McpLastSeen);
        Assert.True(_memberPresence.AnyOnMcp());
        Assert.Equal(0, _backoffice.CountOnline());
    }

    [Fact]
    public async Task Nobody_on_mcp_means_the_table_needs_no_labels()
    {
        Guid alice = _members.Add(1, "Alice");
        _memberPresence.Seen(SignedInWithKey(alice));

        Assert.False(_memberPresence.AnyOnMcp());
        Assert.False(_backoffice.AnyOnMcp());
    }

    [Fact]
    public async Task A_member_signed_in_with_their_numeric_id_is_found_too()
    {
        Guid alice = _members.Add(42, "Alice");

        await CallMcp(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "42")], "Test")));

        Assert.Equal([alice], _memberPresence.AllOnline());
    }

    [Fact]
    public async Task A_member_on_the_website_and_mcp_has_one_row_saying_when_they_were_last_seen_each_way()
    {
        Guid alice = _members.Add(1, "Alice");
        Guid bob = _members.Add(2, "Bob");
        _memberPresence.Seen(SignedInWithKey(alice));
        DateTimeOffset onWebsite = _clock.GetUtcNow();
        _clock.Advance(TimeSpan.FromSeconds(10));
        _memberPresence.Seen(SignedInWithKey(bob));
        _clock.Advance(TimeSpan.FromSeconds(10));

        await CallMcp(SignedInWithKey(alice));

        (int total, IReadOnlyList<OnlineMember> page) = await _memberPresence.PageAsync(0, 10);
        Assert.Equal(2, total);
        Assert.Equal(["Bob", "Alice"], page.Select(m => m.Name));
        OnlineMember row = page.Single(m => m.Key == alice);
        Assert.Equal(onWebsite, row.WebsiteLastSeen);
        Assert.Equal(_clock.GetUtcNow(), row.McpLastSeen);
        Assert.Equal(_clock.GetUtcNow(), row.LastSeen);
        Assert.Equal(2, _memberPresence.CountOnline());
    }

    [Fact]
    public async Task Signing_out_of_the_website_leaves_the_mcp_row()
    {
        Guid alice = _members.Add(1, "Alice");
        _memberPresence.Seen(SignedInWithKey(alice));
        await CallMcp(SignedInWithKey(alice));

        _memberPresence.SignedOut(alice);

        (_, IReadOnlyList<OnlineMember> page) = await _memberPresence.PageAsync(0, 10);
        OnlineMember row = Assert.Single(page);
        Assert.Null(row.WebsiteLastSeen);
        Assert.NotNull(row.McpLastSeen);
    }

    [Fact]
    public async Task A_backoffice_user_on_mcp_is_listed_but_not_messaged_with_everyone_in_the_backoffice()
    {
        Guid alice = _users.Add("Alice");
        Guid bob = _users.Add("Bob");
        _backoffice.Seen(SignedInWithKey(bob));

        await CallMcp(SignedInWithKey(alice));

        (_, IReadOnlyList<OnlineBackofficeUser> page) = await _backoffice.PageAsync(0, 10);
        OnlineBackofficeUser mcpOnly = page.Single(u => u.Key == alice);
        Assert.Null(mcpOnly.BackofficeLastSeen);
        Assert.NotNull(mcpOnly.McpLastSeen);
        Assert.Equal(0, mcpOnly.OpenTabs);
        Assert.False(mcpOnly.IsConnected);
        OnlineBackofficeUser inBackoffice = page.Single(u => u.Key == bob);
        Assert.NotNull(inBackoffice.BackofficeLastSeen);
        Assert.Null(inBackoffice.McpLastSeen);
        Assert.Equal(2, _backoffice.CountOnline());
        Assert.Equal([bob], _backoffice.AllInBackoffice());

        var messenger = new BackofficeMessenger(new NoServerEvents(), _backoffice, _clock);
        Assert.Equal(1, messenger.CountAllOnlineRecipients(senderKey: null));
    }

    [Fact]
    public async Task A_key_that_is_both_a_user_and_a_member_counts_as_the_user()
    {
        var key = Guid.NewGuid();
        _users.Add(key, "Alice in the backoffice");
        _members.Add(1, key, "Alice the member");

        await CallMcp(SignedInWithKey(key));

        Assert.Equal([key], _backoffice.AllOnline());
        Assert.Empty(_memberPresence.AllOnline());
    }

    [Fact]
    public async Task Callers_who_are_not_signed_in_or_not_known_are_not_listed()
    {
        await CallMcp(new ClaimsPrincipal(new ClaimsIdentity()));
        await CallMcp(SignedInWithKey(Guid.NewGuid()));

        Assert.Equal(0, _backoffice.CountOnline());
        Assert.Equal(0, _memberPresence.CountOnline());
    }

    [Fact]
    public async Task A_caller_is_looked_up_once_while_remembered()
    {
        Guid alice = _users.Add("Alice");

        await CallMcp(SignedInWithKey(alice));
        await CallMcp(SignedInWithKey(alice));
        Assert.Equal(1, _users.Lookups);

        _clock.Advance(McpCallers.RememberFor + TimeSpan.FromSeconds(1));
        await CallMcp(SignedInWithKey(alice));
        Assert.Equal(2, _users.Lookups);
    }

    [Fact]
    public async Task An_open_stream_keeps_its_caller_online_until_it_closes_then_the_window_applies()
    {
        Guid alice = _members.Add(1, "Alice");
        var stream = new TaskCompletionSource();
        var response = new StartableResponse();
        Task request = CallMcp(SignedInWithKey(alice), response, async () =>
        {
            await response.StartAsync();
            await stream.Task;
        });

        // Listed as soon as the stream starts, and still listed, as active now, long after the window while it stays open.
        Assert.Equal([alice], _memberPresence.AllOnline());
        _clock.Advance(TimeSpan.FromMinutes(30));
        (_, IReadOnlyList<OnlineMember> page) = await _memberPresence.PageAsync(0, 10);
        Assert.Equal(_clock.GetUtcNow(), Assert.Single(page).McpLastSeen);

        stream.SetResult();
        await request;
        _clock.Advance(TimeSpan.FromSeconds(299));
        Assert.Equal([alice], _memberPresence.AllOnline());
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(_memberPresence.AllOnline());
    }

    [Fact]
    public async Task A_request_is_counted_once_whether_or_not_its_response_started_first()
    {
        Guid alice = _members.Add(1, "Alice");
        var response = new StartableResponse();

        await CallMcp(SignedInWithKey(alice), response);
        await response.StartAsync();
        _clock.Advance(TimeSpan.FromSeconds(301));

        // Starting the response after the request finished must not leave a stream open forever.
        Assert.Empty(_memberPresence.AllOnline());
    }

    [Fact]
    public async Task Requests_outside_mcp_paths_are_not_mcp_callers()
    {
        Guid alice = _members.Add(1, "Alice");

        await CallMcp(SignedInWithKey(alice), path: "/umbraco/delivery/api/v2/content");

        Assert.Empty(_memberPresence.AllOnline());
    }

    [Fact]
    public async Task An_mcp_server_elsewhere_can_report_its_callers()
    {
        Guid alice = _members.Add(1, "Alice");
        Guid bob = _users.Add("Bob");
        ILiveViewPresence presence = _callers;

        await presence.McpCallerSeenAsync(alice);
        await presence.McpCallerSeenAsync(bob);
        await presence.McpCallerSeenAsync(Guid.NewGuid());

        (_, IReadOnlyList<OnlineMember> members) = await _memberPresence.PageAsync(0, 10);
        Assert.NotNull(Assert.Single(members).McpLastSeen);
        Assert.Equal([bob], _backoffice.AllOnline());

        _clock.Advance(TimeSpan.FromSeconds(301));
        Assert.Equal(0, _memberPresence.CountOnline());
        Assert.Equal(0, _backoffice.CountOnline());
    }

    private Task CallMcp(
        ClaimsPrincipal user, StartableResponse? response = null, Func<Task>? handler = null, string path = "/mcp")
    {
        var context = new DefaultHttpContext { User = user };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        if (response is not null)
        {
            context.Features.Set<IHttpResponseFeature>(response);
        }

        return _middleware.InvokeAsync(context, _ => handler?.Invoke() ?? Task.CompletedTask);
    }

    private static ClaimsPrincipal SignedInWithKey(Guid key) =>
        new(new ClaimsIdentity([new Claim("sub", key.ToString())], "Test"));

    /// <summary>A response whose start the test decides, as a server would when the first bytes are sent.</summary>
    private sealed class StartableResponse : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach ((Func<object, Task> callback, object state) in _onStarting)
            {
                await callback(state);
            }

            _onStarting.Clear();
        }
    }

    private sealed class NoTracker : ILiveActivityTracker
    {
        public IDisposable BeginActivity(string channel, string sessionKey) => new Nothing();

        public void Touch(string channel, string sessionKey)
        {
        }

        public void End(string channel, string sessionKey)
        {
        }

        public int CountActive(string channel) => 0;

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class NoServerEvents : IServerEventRouter
    {
        public Task NotifyUserAsync(ServerEvent serverEvent, Guid userKey) => Task.CompletedTask;

        public Task RouteEventAsync(ServerEvent serverEvent) => Task.CompletedTask;

        public Task RouteEventAsync(ServerEvent serverEvent, ServerEventRoutingContext routingContext) => Task.CompletedTask;

        public Task BroadcastEventAsync(ServerEvent serverEvent) => Task.CompletedTask;
    }
}
