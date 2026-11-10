using System.Security.Claims;
using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Tests.Presence;

public class BackofficePresenceTests
{
    private FakeTimeProvider _clock;
    private InMemoryBackofficeUserDirectory _directory;
    private LiveViewOptions _options;
    private BackofficePresence _presence;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        _directory = new InMemoryBackofficeUserDirectory();
        _options = new LiveViewOptions();
        var options = new StaticOptionsMonitor<LiveViewOptions>(_options);
        _presence = new BackofficePresence(new InMemoryActivityStore(options), _directory, options, _clock);
    }

    [Test]
    public async Task A_user_seen_is_online()
    {
        Guid alice = _directory.Add("Alice");

        await _presence.SeenAsync(SignedIn(alice));

        Assert.That(await _presence.CountOnlineAsync(), Is.EqualTo(1));
        Assert.That(await _presence.AllOnlineAsync(), Is.EqualTo([alice]));
    }

    [Test]
    public async Task Requests_that_are_not_signed_in_are_ignored()
    {
        await _presence.SeenAsync(new ClaimsPrincipal(new ClaimsIdentity()));
        await _presence.SeenAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())])));

        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task A_user_without_an_open_tab_goes_offline_once_the_activity_window_passes()
    {
        Guid alice = _directory.Add("Alice");
        await _presence.SeenAsync(SignedIn(alice));

        _clock.Advance(TimeSpan.FromSeconds(299));
        Assert.That(await _presence.CountOnlineAsync(), Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task The_backoffice_channel_can_set_its_own_window()
    {
        _options.Channels[Constants.Channels.Backoffice] = new LiveViewChannelOptions { ActivityWindowSeconds = 30 };
        Guid alice = _directory.Add("Alice");
        await _presence.SeenAsync(SignedIn(alice));

        _clock.Advance(TimeSpan.FromSeconds(31));

        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task An_idle_editor_with_an_open_tab_stays_online_while_it_sends_heartbeats()
    {
        Guid alice = _directory.Add("Alice");
        await _presence.TabHeartbeatAsync(alice, "tab-1");
        await _presence.TabHeartbeatAsync(alice, "tab-2");

        for (int minute = 0; minute < 10; minute++)
        {
            _clock.Advance(TimeSpan.FromSeconds(60));
            await _presence.TabHeartbeatAsync(alice, "tab-1");
        }

        (int total, IReadOnlyList<OnlineBackofficeUser> page) = await _presence.PageAsync(0, 10);

        Assert.That(total, Is.EqualTo(1));
        Assert.That(page, Has.Exactly(1).Items);
        Assert.That(page.Single().OpenTabs, Is.EqualTo(1));
    }

    [Test]
    public async Task A_closed_tab_stops_counting_straight_away()
    {
        Guid alice = _directory.Add("Alice");
        await _presence.TabHeartbeatAsync(alice, "tab-1");
        await _presence.TabHeartbeatAsync(alice, "tab-2");

        await _presence.TabClosedAsync(alice, "tab-1");

        (_, IReadOnlyList<OnlineBackofficeUser> page) = await _presence.PageAsync(0, 10);
        Assert.That(page, Has.Exactly(1).Items);
        Assert.That(page.Single().OpenTabs, Is.EqualTo(1));
    }

    [Test]
    public async Task A_tab_that_stops_sending_heartbeats_times_out_and_then_the_window_applies()
    {
        _options.Channels[Constants.Channels.Backoffice] = new LiveViewChannelOptions { ActivityWindowSeconds = 30 };
        Guid alice = _directory.Add("Alice");
        await _presence.TabHeartbeatAsync(alice, "tab-1");

        _clock.Advance(BackofficePresence.TabTimeout);
        Assert.That(await _presence.CountOnlineAsync(), Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task Tabs_beyond_the_limit_are_not_counted()
    {
        Guid alice = _directory.Add("Alice");
        for (int tab = 0; tab <= BackofficePresence.MaxTabsPerUser; tab++)
        {
            await _presence.TabHeartbeatAsync(alice, $"tab-{tab}");
        }

        (_, IReadOnlyList<OnlineBackofficeUser> page) = await _presence.PageAsync(0, 10);
        Assert.That(page, Has.Exactly(1).Items);
        Assert.That(page.Single().OpenTabs, Is.EqualTo(BackofficePresence.MaxTabsPerUser));
    }

    [Test]
    public async Task Realtime_connections_count_the_tabs_when_the_user_has_any()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        await _presence.TabHeartbeatAsync(alice, "tab-1");
        await _presence.TabHeartbeatAsync(bob, "tab-1");
        await _presence.TabHeartbeatAsync(bob, "tab-2");
        _directory.SetConnections(alice, 3);

        (_, IReadOnlyList<OnlineBackofficeUser> page) = await _presence.PageAsync(0, 10);

        OnlineBackofficeUser aliceRow = page.Single(u => u.Key == alice);
        OnlineBackofficeUser bobRow = page.Single(u => u.Key == bob);
        Assert.That((aliceRow.OpenTabs, aliceRow.IsConnected), Is.EqualTo((3, true)));
        Assert.That((bobRow.OpenTabs, bobRow.IsConnected), Is.EqualTo((2, false)));
    }

    [Test]
    public async Task Pages_list_newest_arrivals_first_and_keep_their_order_as_users_work()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        Guid carol = _directory.Add("Carol");
        foreach (Guid user in new[] { alice, bob, carol })
        {
            await _presence.SeenAsync(SignedIn(user));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        await _presence.SeenAsync(SignedIn(alice));

        (int total, IReadOnlyList<OnlineBackofficeUser> first) = await _presence.PageAsync(0, 2);
        (_, IReadOnlyList<OnlineBackofficeUser> second) = await _presence.PageAsync(2, 2);

        Assert.That(total, Is.EqualTo(3));
        Assert.That(first.Select(u => u.Name), Is.EqualTo(["Carol", "Bob"]));
        Assert.That(second.Select(u => u.Name), Is.EqualTo(["Alice"]));
        Assert.That(second[0].LastSeen, Is.EqualTo(_clock.GetUtcNow()));
    }

    [Test]
    public async Task Rows_carry_the_users_details()
    {
        Guid alice = _directory.Add("Alice", "alice@example.test");
        await _presence.SeenAsync(SignedIn(alice));

        (_, IReadOnlyList<OnlineBackofficeUser> page) = await _presence.PageAsync(0, 10);

        Assert.That(page, Has.Exactly(1).Items);
        OnlineBackofficeUser row = page.Single();
        Assert.That((row.Key, row.Name, row.Email, row.OpenTabs, row.IsConnected), Is.EqualTo((alice, "Alice", "alice@example.test", 0, false)));
    }

    [Test]
    public async Task Users_whose_account_has_gone_or_is_disabled_are_left_out_of_the_page_and_the_total()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        Guid carol = _directory.Add("Carol");
        Guid dave = _directory.Add("Dave");
        foreach (Guid user in new[] { alice, bob, carol, dave })
        {
            await _presence.TabHeartbeatAsync(user, "tab-1");
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        _directory.Remove(dave);
        _directory.Remove(carol);

        (int total, IReadOnlyList<OnlineBackofficeUser> page) = await _presence.PageAsync(0, 2);

        Assert.That(total, Is.EqualTo(2));
        Assert.That(page.Select(u => u.Name), Is.EqualTo(["Bob", "Alice"]));
        Assert.That(await _presence.CountOnlineAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task A_forgotten_user_is_offline_even_with_an_open_tab()
    {
        Guid alice = _directory.Add("Alice");
        await _presence.TabHeartbeatAsync(alice, "tab-1");

        await _presence.ForgetAsync(alice);

        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task All_online_includes_users_on_every_page_newest_arrivals_first()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        Guid carol = _directory.Add("Carol");
        await _presence.SeenAsync(SignedIn(alice));
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _presence.TabHeartbeatAsync(bob, "tab-1");
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _presence.SeenAsync(SignedIn(carol));

        Assert.That(await _presence.AllOnlineAsync(), Is.EqualTo([carol, bob, alice]));
    }

    [Test]
    public async Task The_count_and_the_table_agree_on_who_is_online()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        Guid carol = _directory.Add("Carol");
        await _presence.TabHeartbeatAsync(alice, "tab-1");
        await _presence.SeenAsync(SignedIn(bob));
        await _presence.SeenAsync(SignedIn(carol));

        // Bob's window passes; Alice keeps her tab open and Carol keeps working.
        for (int minute = 0; minute < 6; minute++)
        {
            _clock.Advance(TimeSpan.FromSeconds(60));
            await _presence.TabHeartbeatAsync(alice, "tab-1");
            await _presence.SeenAsync(SignedIn(carol));
        }

        (int total, IReadOnlyList<OnlineBackofficeUser> page) = await _presence.PageAsync(0, 10);

        Assert.That(total, Is.EqualTo(2));
        Assert.That(await _presence.CountOnlineAsync(), Is.EqualTo(total));
        Assert.That(await _presence.AllOnlineAsync(), Is.EqualTo(page.Select(u => u.Key)));
    }

    [Test]
    public async Task Each_row_carries_the_users_avatar_sizes()
    {
        string[] sizes = ["/media/a.jpg?width=30", "/media/a.jpg?width=60"];
        Guid alice = _directory.Add("Alice", avatarUrls: sizes);
        Guid bob = _directory.Add("Bob");
        await _presence.SeenAsync(SignedIn(alice));
        await _presence.SeenAsync(SignedIn(bob));

        (_, IReadOnlyList<OnlineBackofficeUser> page) = await _presence.PageAsync(0, 10);

        Assert.That(page.Single(u => u.Key == alice).AvatarUrls, Is.EqualTo(sizes));
        Assert.That(page.Single(u => u.Key == bob).AvatarUrls, Is.Empty);
    }

    private static ClaimsPrincipal SignedIn(Guid key) =>
        new(new ClaimsIdentity([new Claim("sub", key.ToString())], "Test"));
}
