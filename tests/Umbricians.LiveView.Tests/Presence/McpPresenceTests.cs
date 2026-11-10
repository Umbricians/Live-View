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
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Presence;

/// <summary>People using MCP, through the tracking middleware and the push interface, as the tables see them.</summary>
public class McpPresenceTests
{
    private FakeTimeProvider _clock;
    private InMemoryBackofficeUserDirectory _users;
    private InMemoryMemberDirectory _members;
    private BackofficePresence _backoffice;
    private MemberPresence _memberPresence;
    private McpCallers _callers;
    private LiveActivityMiddleware _middleware;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        _users = new InMemoryBackofficeUserDirectory();
        _members = new InMemoryMemberDirectory();
        var liveViewOptions = new LiveViewOptions();
        liveViewOptions.Channels[Constants.Channels.Mcp] = new LiveViewChannelOptions
        {
            PathPrefixes = ["/mcp", "/umbraco/mcp"],
            SessionKeys = [Constants.SessionKeySources.HeaderPrefix + "Mcp-Session-Id", Constants.SessionKeySources.Client],
        };
        var options = new StaticOptionsMonitor<LiveViewOptions>(liveViewOptions);
        var store = new InMemoryActivityStore(options);
        _backoffice = new BackofficePresence(store, _users, options, _clock);
        _memberPresence = new MemberPresence(store, _members, options, _clock);
        _callers = new McpCallers(_backoffice, _memberPresence, _users, _members, _clock, NullLogger<McpCallers>.Instance);
        _middleware = new LiveActivityMiddleware(new RequestClassifier(options, [], []), new NoTracker(), _backoffice, _memberPresence, _callers, new UnforwardedProxyDetector());
    }

    [Test]
    public async Task A_member_on_mcp_is_listed_as_on_mcp_only()
    {
        Guid alice = _members.Add(1, "Alice");

        await CallMcp(SignedInWithKey(alice));

        (int total, IReadOnlyList<OnlineMember> page) = await _memberPresence.PageAsync(0, 10);
        Assert.That(page, Has.Exactly(1).Items);
        OnlineMember row = page.Single();
        Assert.That(total, Is.EqualTo(1));
        Assert.That(row.WebsiteLastSeen, Is.Null);
        Assert.That(row.McpLastSeen, Is.EqualTo(_clock.GetUtcNow()));
        Assert.That(await _memberPresence.AnyOnMcpAsync(), Is.True);
        Assert.That(await _backoffice.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task Nobody_on_mcp_means_the_table_needs_no_labels()
    {
        Guid alice = _members.Add(1, "Alice");
        await _memberPresence.SeenAsync(SignedInWithKey(alice));

        Assert.That(await _memberPresence.AnyOnMcpAsync(), Is.False);
        Assert.That(await _backoffice.AnyOnMcpAsync(), Is.False);
    }

    [Test]
    public async Task A_member_signed_in_with_their_numeric_id_is_found_too()
    {
        Guid alice = _members.Add(42, "Alice");

        await CallMcp(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "42")], "Test")));

        Assert.That(await _memberPresence.AllOnlineAsync(), Is.EqualTo([alice]));
    }

    [Test]
    public async Task A_member_on_the_website_and_mcp_has_one_row_saying_when_they_were_last_seen_each_way()
    {
        Guid alice = _members.Add(1, "Alice");
        Guid bob = _members.Add(2, "Bob");
        await _memberPresence.SeenAsync(SignedInWithKey(alice));
        DateTimeOffset onWebsite = _clock.GetUtcNow();
        _clock.Advance(TimeSpan.FromSeconds(10));
        await _memberPresence.SeenAsync(SignedInWithKey(bob));
        _clock.Advance(TimeSpan.FromSeconds(10));

        await CallMcp(SignedInWithKey(alice));

        (int total, IReadOnlyList<OnlineMember> page) = await _memberPresence.PageAsync(0, 10);
        Assert.That(total, Is.EqualTo(2));
        Assert.That(page.Select(m => m.Name), Is.EqualTo(["Bob", "Alice"]));
        OnlineMember row = page.Single(m => m.Key == alice);
        Assert.That(row.WebsiteLastSeen, Is.EqualTo(onWebsite));
        Assert.That(row.McpLastSeen, Is.EqualTo(_clock.GetUtcNow()));
        Assert.That(row.LastSeen, Is.EqualTo(_clock.GetUtcNow()));
        Assert.That(await _memberPresence.CountOnlineAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Signing_out_of_the_website_leaves_the_mcp_row()
    {
        Guid alice = _members.Add(1, "Alice");
        await _memberPresence.SeenAsync(SignedInWithKey(alice));
        await CallMcp(SignedInWithKey(alice));

        await _memberPresence.SignedOutAsync(alice);

        (_, IReadOnlyList<OnlineMember> page) = await _memberPresence.PageAsync(0, 10);
        Assert.That(page, Has.Exactly(1).Items);
        OnlineMember row = page.Single();
        Assert.That(row.WebsiteLastSeen, Is.Null);
        Assert.That(row.McpLastSeen, Is.Not.Null);
    }

    [Test]
    public async Task A_backoffice_user_on_mcp_is_listed_but_not_messaged_with_everyone_in_the_backoffice()
    {
        Guid alice = _users.Add("Alice");
        Guid bob = _users.Add("Bob");
        await _backoffice.SeenAsync(SignedInWithKey(bob));

        await CallMcp(SignedInWithKey(alice));

        (_, IReadOnlyList<OnlineBackofficeUser> page) = await _backoffice.PageAsync(0, 10);
        OnlineBackofficeUser mcpOnly = page.Single(u => u.Key == alice);
        Assert.That(mcpOnly.BackofficeLastSeen, Is.Null);
        Assert.That(mcpOnly.McpLastSeen, Is.Not.Null);
        Assert.That(mcpOnly.OpenTabs, Is.Zero);
        Assert.That(mcpOnly.IsConnected, Is.False);
        OnlineBackofficeUser inBackoffice = page.Single(u => u.Key == bob);
        Assert.That(inBackoffice.BackofficeLastSeen, Is.Not.Null);
        Assert.That(inBackoffice.McpLastSeen, Is.Null);
        Assert.That(await _backoffice.CountOnlineAsync(), Is.EqualTo(2));
        Assert.That(await _backoffice.AllInBackofficeAsync(), Is.EqualTo([bob]));

        var messenger = new BackofficeMessenger(
            new NoServerEvents(),
            _backoffice,
            _users,
            new InMemoryBackofficeMessageStore(_clock),
            new RecordingEventAggregator(),
            new StaticOptionsMonitor<LiveViewOptions>(new LiveViewOptions()),
            _clock);
        Assert.That(await messenger.CountAllOnlineRecipientsAsync(senderKey: null), Is.EqualTo(1));
    }

    [Test]
    public async Task A_key_that_is_both_a_user_and_a_member_counts_as_the_user()
    {
        var key = Guid.NewGuid();
        _users.Add(key, "Alice in the backoffice");
        _members.Add(1, key, "Alice the member");

        await CallMcp(SignedInWithKey(key));

        Assert.That(await _backoffice.AllOnlineAsync(), Is.EqualTo([key]));
        Assert.That(await _memberPresence.AllOnlineAsync(), Is.Empty);
    }

    [Test]
    public async Task Callers_who_are_not_signed_in_or_not_known_are_not_listed()
    {
        await CallMcp(new ClaimsPrincipal(new ClaimsIdentity()));
        await CallMcp(SignedInWithKey(Guid.NewGuid()));

        Assert.That(await _backoffice.CountOnlineAsync(), Is.Zero);
        Assert.That(await _memberPresence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task A_caller_is_looked_up_once_while_remembered()
    {
        Guid alice = _users.Add("Alice");

        await CallMcp(SignedInWithKey(alice));
        await CallMcp(SignedInWithKey(alice));
        Assert.That(_users.Lookups, Is.EqualTo(1));

        _clock.Advance(McpCallers.RememberFor + TimeSpan.FromSeconds(1));
        await CallMcp(SignedInWithKey(alice));
        Assert.That(_users.Lookups, Is.EqualTo(2));
    }

    [Test]
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
        Assert.That(await _memberPresence.AllOnlineAsync(), Is.EqualTo([alice]));
        _clock.Advance(TimeSpan.FromMinutes(30));
        (_, IReadOnlyList<OnlineMember> page) = await _memberPresence.PageAsync(0, 10);
        Assert.That(page, Has.Exactly(1).Items);
        Assert.That(page.Single().McpLastSeen, Is.EqualTo(_clock.GetUtcNow()));

        stream.SetResult();
        await request;
        _clock.Advance(TimeSpan.FromSeconds(299));
        Assert.That(await _memberPresence.AllOnlineAsync(), Is.EqualTo([alice]));
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That(await _memberPresence.AllOnlineAsync(), Is.Empty);
    }

    [Test]
    public async Task A_stream_that_fails_lets_its_caller_go_once_the_window_has_passed()
    {
        Guid alice = _members.Add(1, "Alice");
        var response = new StartableResponse();

        await Assert.ThatAsync(() => CallMcp(SignedInWithKey(alice), response, async () =>
        {
            await response.StartAsync();
            throw new InvalidOperationException("The stream broke.");
        }), Throws.TypeOf<InvalidOperationException>());

        _clock.Advance(TimeSpan.FromSeconds(301));
        Assert.That(await _memberPresence.AllOnlineAsync(), Is.Empty);
    }

    [Test]
    public async Task A_request_is_counted_once_whether_or_not_its_response_started_first()
    {
        Guid alice = _members.Add(1, "Alice");
        var response = new StartableResponse();

        await CallMcp(SignedInWithKey(alice), response);
        await response.StartAsync();
        _clock.Advance(TimeSpan.FromSeconds(301));

        // Starting the response after the request finished must not leave a stream open forever.
        Assert.That(await _memberPresence.AllOnlineAsync(), Is.Empty);
    }

    [Test]
    public async Task Requests_outside_mcp_paths_are_not_mcp_callers()
    {
        Guid alice = _members.Add(1, "Alice");

        await CallMcp(SignedInWithKey(alice), path: "/umbraco/delivery/api/v2/content");

        Assert.That(await _memberPresence.AllOnlineAsync(), Is.Empty);
    }

    [Test]
    public async Task An_mcp_server_elsewhere_can_report_its_callers()
    {
        Guid alice = _members.Add(1, "Alice");
        Guid bob = _users.Add("Bob");
        ILiveViewPresence presence = _callers;

        await presence.McpCallerSeenAsync(alice);
        await presence.McpCallerSeenAsync(bob);
        await presence.McpCallerSeenAsync(Guid.NewGuid());

        (_, IReadOnlyList<OnlineMember> members) = await _memberPresence.PageAsync(0, 10);
        Assert.That(members, Has.Exactly(1).Items);
        Assert.That(members.Single().McpLastSeen, Is.Not.Null);
        Assert.That(await _backoffice.AllOnlineAsync(), Is.EqualTo([bob]));

        _clock.Advance(TimeSpan.FromSeconds(301));
        Assert.That(await _memberPresence.CountOnlineAsync(), Is.Zero);
        Assert.That(await _backoffice.CountOnlineAsync(), Is.Zero);
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
        public Task<IAsyncDisposable> BeginActivityAsync(string channel, string sessionKey) =>
            Task.FromResult<IAsyncDisposable>(new Nothing());

        public Task TouchAsync(string channel, string sessionKey) => Task.CompletedTask;

        public Task EndAsync(string channel, string sessionKey) => Task.CompletedTask;

        public Task<int> CountActiveAsync(string channel) => Task.FromResult(0);

        private sealed class Nothing : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
