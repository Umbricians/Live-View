using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;
using Umbricians.LiveView.Composers;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Storage;

/// <summary>
/// Two servers are two sets of modules with their own in-process state, over one store a site shares between them.
/// </summary>
public class SharedStoreTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly StaticOptionsMonitor<LiveViewOptions> _options = new(new LiveViewOptions());
    private readonly SharedActivityStore _activity = new();
    private readonly InMemoryBackofficeUserDirectory _users = new();
    private readonly InMemoryMemberDirectory _members = new();

    [Fact]
    public async Task A_session_tracked_on_one_server_is_counted_on_another()
    {
        var a = new LiveActivityTracker(_activity, _options, _clock);
        var b = new LiveActivityTracker(_activity, _options, _clock);

        await a.TouchAsync("web", "session-1");

        Assert.Equal(1, await b.CountActiveAsync("web"));

        await a.EndAsync("web", "session-1");
        Assert.Equal(0, await b.CountActiveAsync("web"));

        IAsyncDisposable open = await a.BeginActivityAsync("web", "session-2");
        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(1, await b.CountActiveAsync("web"));

        await open.DisposeAsync();
        Assert.Equal(1, await b.CountActiveAsync("web"));
        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0, await b.CountActiveAsync("web"));
    }

    [Fact]
    public async Task The_tabs_of_every_online_user_are_counted_in_one_store_call()
    {
        BackofficePresence presence = NewBackoffice();
        foreach (string name in new[] { "Alice", "Bob", "Carol" })
        {
            await presence.TabHeartbeatAsync(_users.Add(name), "tab-1");
        }

        _activity.GetActiveCalls.Clear();
        _activity.CountActiveCalls.Clear();

        Assert.Equal(3, await presence.CountOnlineAsync());

        Assert.Equal(1, _activity.GetActiveCalls.Count(set => set == ActivitySets.BackofficeUsers));
        Assert.Equal(3, _activity.CountActiveCalls.Single().Count);
    }

    [Fact]
    public async Task A_backoffice_user_seen_on_one_server_is_online_on_another()
    {
        BackofficePresence a = NewBackoffice();
        BackofficePresence b = NewBackoffice();
        Guid alice = _users.Add("Alice");

        await a.TabHeartbeatAsync(alice, "tab-1");

        Assert.Equal(1, await b.CountOnlineAsync());
        (_, IReadOnlyList<OnlineBackofficeUser> page) = await b.PageAsync(0, 10);
        Assert.Equal(1, page.Single().OpenTabs);

        await b.TabClosedAsync(alice, "tab-1");
        (_, page) = await a.PageAsync(0, 10);
        Assert.Equal(0, page.Single().OpenTabs);
        Assert.Equal([alice], await a.AllInBackofficeAsync());

        await a.ForgetAsync(alice);
        Assert.Equal(0, await b.CountOnlineAsync());
    }

    [Fact]
    public async Task A_member_seen_on_one_server_is_online_on_another_and_signing_out_on_either_takes_them_off()
    {
        MemberPresence a = NewMembers();
        MemberPresence b = NewMembers();
        Guid alice = _members.Add(1, "Alice");

        await a.SeenAsync(SignedInWithKey(alice));

        Assert.Equal([alice], await b.AllOnlineAsync());

        await b.SignedOutAsync(alice);
        Assert.Equal(0, await a.CountOnlineAsync());

        _clock.Advance(MemberPresence.SignedOutMarkerLifetime + TimeSpan.FromSeconds(1));
        await a.SeenAsync(SignedInWithKey(alice));
        await a.SignedOutAsync(alice);
        Assert.Equal(0, await b.CountOnlineAsync());
    }

    [Fact]
    public async Task An_mcp_caller_on_one_server_is_listed_on_another()
    {
        Guid alice = _users.Add("Alice");
        (BackofficePresence backofficeA, McpCallers a) = NewCallers();
        (BackofficePresence backofficeB, _) = NewCallers();

        await a.McpCallerSeenAsync(alice);

        Assert.Equal(1, await backofficeB.CountOnlineAsync());
        (_, IReadOnlyList<OnlineBackofficeUser> page) = await backofficeB.PageAsync(0, 10);
        OnlineBackofficeUser row = page.Single();
        Assert.Equal(alice, row.Key);
        Assert.NotNull(row.McpLastSeen);
        Assert.Null(row.BackofficeLastSeen);
        Assert.True(await backofficeA.AnyOnMcpAsync());
    }

    [Fact]
    public async Task A_message_sent_on_one_server_is_picked_up_from_a_heartbeat_on_another()
    {
        var store = new SharedBackofficeMessageStore();
        Guid me = _users.Add("Me");
        Guid alice = _users.Add("Alice");
        Guid bob = _users.Add("Bob");
        BackofficeMessenger a = new(new NoServerEvents(), NewBackoffice(), _users, store, new RecordingEventAggregator(), _options, _clock);
        BackofficeMessenger b = new(new NoServerEvents(), NewBackoffice(), _users, store, new RecordingEventAggregator(), _options, _clock);

        var request = new BackofficeMessageRequest("Heads up", "Restarting soon", "info", [alice], null, AllOnline: false);
        var result = Assert.IsType<BackofficeSendResult.Sent>(await a.SendAsync(request, new BackofficeSender(me, "Me")));
        Guid key = result.Message.Key;

        Assert.Equal([key], await b.GetUndeliveredAsync(alice));
        Assert.Null(await b.ReceiveAsync(key, bob));
        Assert.Equal("Heads up", (await b.ReceiveAsync(key, alice))?.Headline);
        Assert.Empty(await b.GetUndeliveredAsync(alice));
        Assert.Empty(await a.GetUndeliveredAsync(alice));
    }

    [Fact]
    public async Task A_sites_store_is_used_whichever_order_it_is_registered_in()
    {
        var before = new ServiceCollection();
        var siteActivity = new SharedActivityStore();
        var siteMessages = new SharedBackofficeMessageStore();
        before.AddSingleton<ILiveViewActivityStore>(siteActivity);
        before.AddSingleton<IBackofficeMessageStore>(siteMessages);
        before.AddLiveView(new ConfigurationBuilder().Build());

        var after = new ServiceCollection();
        after.AddLiveView(new ConfigurationBuilder().Build());
        after.AddSingleton<ILiveViewActivityStore>(siteActivity);
        after.AddSingleton<IBackofficeMessageStore>(siteMessages);

        foreach (ServiceCollection services in new[] { before, after })
        {
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IOptionsMonitor<LiveViewOptions>>(_options);
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Same(siteActivity, provider.GetRequiredService<ILiveViewActivityStore>());
            Assert.Same(siteMessages, provider.GetRequiredService<IBackofficeMessageStore>());

            // The tracker keeps its sessions in the site's store.
            await provider.GetRequiredService<ILiveActivityTracker>().TouchAsync("web", "session-1");
            Assert.Equal(1, (await siteActivity.CountActiveAsync([ActivitySets.Channel("web")], DateTimeOffset.MinValue))[0]);
        }
    }

    private BackofficePresence NewBackoffice() => new(_activity, _users, _options, _clock);

    private MemberPresence NewMembers() => new(_activity, _members, _options, _clock);

    private (BackofficePresence Backoffice, McpCallers Callers) NewCallers()
    {
        BackofficePresence backoffice = NewBackoffice();
        MemberPresence members = NewMembers();
        return (backoffice, new McpCallers(backoffice, members, _users, _members, _clock, NullLogger<McpCallers>.Instance));
    }

    private static ClaimsPrincipal SignedInWithKey(Guid key) =>
        new(new ClaimsIdentity([new Claim("sub", key.ToString())], "Test"));

    private sealed class NoServerEvents : IServerEventRouter
    {
        public Task NotifyUserAsync(ServerEvent serverEvent, Guid userKey) => Task.CompletedTask;

        public Task RouteEventAsync(ServerEvent serverEvent) => Task.CompletedTask;

        public Task RouteEventAsync(ServerEvent serverEvent, ServerEventRoutingContext routingContext) => Task.CompletedTask;

        public Task BroadcastEventAsync(ServerEvent serverEvent) => Task.CompletedTask;
    }
}
