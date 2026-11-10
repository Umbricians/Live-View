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
    private FakeTimeProvider _clock;
    private StaticOptionsMonitor<LiveViewOptions> _options;
    private SharedActivityStore _activity;
    private InMemoryBackofficeUserDirectory _users;
    private InMemoryMemberDirectory _members;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        _options = new StaticOptionsMonitor<LiveViewOptions>(new LiveViewOptions());
        _activity = new SharedActivityStore();
        _users = new InMemoryBackofficeUserDirectory();
        _members = new InMemoryMemberDirectory();
    }

    [Test]
    public async Task A_session_tracked_on_one_server_is_counted_on_another()
    {
        var a = new LiveActivityTracker(_activity, _options, _clock);
        var b = new LiveActivityTracker(_activity, _options, _clock);

        await a.TouchAsync("web", "session-1");

        Assert.That(await b.CountActiveAsync("web"), Is.EqualTo(1));

        await a.EndAsync("web", "session-1");
        Assert.That(await b.CountActiveAsync("web"), Is.Zero);

        IAsyncDisposable open = await a.BeginActivityAsync("web", "session-2");
        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.That(await b.CountActiveAsync("web"), Is.EqualTo(1));

        await open.DisposeAsync();
        Assert.That(await b.CountActiveAsync("web"), Is.EqualTo(1));
        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.That(await b.CountActiveAsync("web"), Is.Zero);
    }

    [Test]
    public async Task The_tabs_of_every_online_user_are_counted_in_one_store_call()
    {
        BackofficePresence presence = NewBackoffice();
        foreach (string name in new[] { "Alice", "Bob", "Carol" })
        {
            await presence.TabHeartbeatAsync(_users.Add(name), "tab-1");
        }

        _activity.GetActiveCalls.Clear();
        _activity.CountActiveCalls.Clear();

        Assert.That(await presence.CountOnlineAsync(), Is.EqualTo(3));

        Assert.That(_activity.GetActiveCalls.Count(set => set == ActivitySets.BackofficeUsers), Is.EqualTo(1));
        Assert.That(_activity.CountActiveCalls.Single(), Has.Count.EqualTo(3));
    }

    [Test]
    public async Task A_backoffice_user_seen_on_one_server_is_online_on_another()
    {
        BackofficePresence a = NewBackoffice();
        BackofficePresence b = NewBackoffice();
        Guid alice = _users.Add("Alice");

        await a.TabHeartbeatAsync(alice, "tab-1");

        Assert.That(await b.CountOnlineAsync(), Is.EqualTo(1));
        (_, IReadOnlyList<OnlineBackofficeUser> page) = await b.PageAsync(0, 10);
        Assert.That(page.Single().OpenTabs, Is.EqualTo(1));

        await b.TabClosedAsync(alice, "tab-1");
        (_, page) = await a.PageAsync(0, 10);
        Assert.That(page.Single().OpenTabs, Is.Zero);
        Assert.That(await a.AllInBackofficeAsync(), Is.EqualTo([alice]));

        await a.ForgetAsync(alice);
        Assert.That(await b.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task A_member_seen_on_one_server_is_online_on_another_and_signing_out_on_either_takes_them_off()
    {
        MemberPresence a = NewMembers();
        MemberPresence b = NewMembers();
        Guid alice = _members.Add(1, "Alice");

        await a.SeenAsync(SignedInWithKey(alice));

        Assert.That(await b.AllOnlineAsync(), Is.EqualTo([alice]));

        await b.SignedOutAsync(alice);
        Assert.That(await a.CountOnlineAsync(), Is.Zero);

        _clock.Advance(MemberPresence.SignedOutMarkerLifetime + TimeSpan.FromSeconds(1));
        await a.SeenAsync(SignedInWithKey(alice));
        await a.SignedOutAsync(alice);
        Assert.That(await b.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task An_mcp_caller_on_one_server_is_listed_on_another()
    {
        Guid alice = _users.Add("Alice");
        (BackofficePresence backofficeA, McpCallers a) = NewCallers();
        (BackofficePresence backofficeB, _) = NewCallers();

        await a.McpCallerSeenAsync(alice);

        Assert.That(await backofficeB.CountOnlineAsync(), Is.EqualTo(1));
        (_, IReadOnlyList<OnlineBackofficeUser> page) = await backofficeB.PageAsync(0, 10);
        OnlineBackofficeUser row = page.Single();
        Assert.That(row.Key, Is.EqualTo(alice));
        Assert.That(row.McpLastSeen, Is.Not.Null);
        Assert.That(row.BackofficeLastSeen, Is.Null);
        Assert.That(await backofficeA.AnyOnMcpAsync(), Is.True);
    }

    [Test]
    public async Task A_message_sent_on_one_server_is_picked_up_from_a_heartbeat_on_another()
    {
        var store = new SharedBackofficeMessageStore();
        Guid me = _users.Add("Me");
        Guid alice = _users.Add("Alice");
        Guid bob = _users.Add("Bob");
        BackofficeMessenger a = new(new NoServerEvents(), NewBackoffice(), _users, store, new RecordingEventAggregator(), _options, _clock);
        BackofficeMessenger b = new(new NoServerEvents(), NewBackoffice(), _users, store, new RecordingEventAggregator(), _options, _clock);

        var request = new BackofficeMessageRequest("Heads up", "Restarting soon", "info", [alice], null, AllOnline: false);
        BackofficeSendResult sent = await a.SendAsync(request, new BackofficeSender(me, "Me"));
        Assert.That(sent, Is.TypeOf<BackofficeSendResult.Sent>());
        var result = (BackofficeSendResult.Sent)sent;
        Guid key = result.Message.Key;

        Assert.That(await b.GetUndeliveredAsync(alice), Is.EqualTo([key]));
        Assert.That(await b.ReceiveAsync(key, bob), Is.Null);
        Assert.That((await b.ReceiveAsync(key, alice))?.Headline, Is.EqualTo("Heads up"));
        Assert.That(await b.GetUndeliveredAsync(alice), Is.Empty);
        Assert.That(await a.GetUndeliveredAsync(alice), Is.Empty);
    }

    [Test]
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

            Assert.That(provider.GetRequiredService<ILiveViewActivityStore>(), Is.SameAs(siteActivity));
            Assert.That(provider.GetRequiredService<IBackofficeMessageStore>(), Is.SameAs(siteMessages));

            // The tracker keeps its sessions in the site's store.
            await provider.GetRequiredService<ILiveActivityTracker>().TouchAsync("web", "session-1");
            Assert.That((await siteActivity.CountActiveAsync([ActivitySets.Channel("web")], DateTimeOffset.MinValue))[0], Is.EqualTo(1));
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
