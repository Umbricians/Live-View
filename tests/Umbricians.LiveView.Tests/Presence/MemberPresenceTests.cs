using System.Security.Claims;
using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Tests.Presence;

public class MemberPresenceTests
{
    private FakeTimeProvider _clock;
    private InMemoryMemberDirectory _directory;
    private LiveViewOptions _options;
    private MemberPresence _presence;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        _directory = new InMemoryMemberDirectory();
        _options = new LiveViewOptions();
        var options = new StaticOptionsMonitor<LiveViewOptions>(_options);
        _presence = new MemberPresence(new InMemoryActivityStore(options), _directory, options, _clock);
    }

    [Test]
    public async Task A_member_seen_by_key_is_online()
    {
        Guid alice = _directory.Add(1, "Alice");

        await _presence.SeenAsync(SignedInWithKey(alice));

        Assert.That(await _presence.CountOnlineAsync(), Is.EqualTo(1));
        Assert.That(await _presence.AllOnlineAsync(), Is.EqualTo([alice]));
    }

    [Test]
    public async Task A_member_seen_by_numeric_id_is_held_by_their_key()
    {
        Guid alice = _directory.Add(42, "Alice");

        await _presence.SeenAsync(SignedInWithId("42"));
        await _presence.SeenAsync(SignedInWithKey(alice));

        Assert.That(await _presence.AllOnlineAsync(), Is.EqualTo([alice]));
    }

    [Test]
    public async Task Visitors_who_are_not_signed_in_or_not_members_are_ignored()
    {
        await _presence.SeenAsync(new ClaimsPrincipal(new ClaimsIdentity()));
        await _presence.SeenAsync(SignedInWithId("999"));
        await _presence.SeenAsync(SignedInWithId("not-an-id"));

        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task A_member_goes_offline_once_the_activity_window_passes()
    {
        Guid alice = _directory.Add(1, "Alice");
        await _presence.SeenAsync(SignedInWithKey(alice));

        _clock.Advance(TimeSpan.FromSeconds(299));
        Assert.That(await _presence.CountOnlineAsync(), Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task The_members_channel_can_set_its_own_window()
    {
        _options.Channels[Constants.Channels.Members] = new LiveViewChannelOptions { ActivityWindowSeconds = 30 };
        Guid alice = _directory.Add(1, "Alice");
        await _presence.SeenAsync(SignedInWithKey(alice));

        _clock.Advance(TimeSpan.FromSeconds(31));

        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task Signing_out_takes_a_member_offline_and_their_sign_out_request_does_not_bring_them_back()
    {
        Guid alice = _directory.Add(1, "Alice");
        await _presence.SeenAsync(SignedInWithKey(alice));

        await _presence.SignedOutAsync(alice);
        await _presence.SeenAsync(SignedInWithKey(alice));

        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task A_member_who_signs_back_in_after_the_marker_expires_is_online_again()
    {
        Guid alice = _directory.Add(1, "Alice");
        await _presence.SignedOutAsync(alice);

        _clock.Advance(MemberPresence.SignedOutMarkerLifetime);
        await _presence.SeenAsync(SignedInWithKey(alice));

        Assert.That(await _presence.CountOnlineAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task A_forgotten_member_is_offline()
    {
        Guid alice = _directory.Add(1, "Alice");
        await _presence.SeenAsync(SignedInWithKey(alice));

        await _presence.ForgetAsync(alice);

        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
    }

    [Test]
    public async Task Pages_list_newest_arrivals_first_and_keep_their_order_as_members_browse()
    {
        Guid alice = _directory.Add(1, "Alice");
        Guid bob = _directory.Add(2, "Bob");
        Guid carol = _directory.Add(3, "Carol");
        foreach (Guid member in new[] { alice, bob, carol })
        {
            await _presence.SeenAsync(SignedInWithKey(member));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        await _presence.SeenAsync(SignedInWithKey(alice));

        (int total, IReadOnlyList<OnlineMember> first) = await _presence.PageAsync(0, 2);
        (_, IReadOnlyList<OnlineMember> second) = await _presence.PageAsync(2, 2);

        Assert.That(total, Is.EqualTo(3));
        Assert.That(first.Select(m => m.Name), Is.EqualTo(["Carol", "Bob"]));
        Assert.That(second.Select(m => m.Name), Is.EqualTo(["Alice"]));
        Assert.That(second[0].LastSeen, Is.EqualTo(_clock.GetUtcNow()));
    }

    [Test]
    public async Task Rows_carry_the_members_details()
    {
        Guid alice = _directory.Add(1, "Alice", "alice@example.test", "Member");
        await _presence.SeenAsync(SignedInWithKey(alice));

        (_, IReadOnlyList<OnlineMember> page) = await _presence.PageAsync(0, 10);

        Assert.That(page, Has.Exactly(1).Items);
        OnlineMember row = page.Single();
        Assert.That((row.Key, row.Name, row.Email, row.MemberType), Is.EqualTo((alice, "Alice", "alice@example.test", "Member")));
    }

    [Test]
    public async Task Members_whose_account_has_gone_are_left_out_of_the_page_and_the_total()
    {
        Guid alice = _directory.Add(1, "Alice");
        Guid bob = _directory.Add(2, "Bob");
        Guid carol = _directory.Add(3, "Carol");
        foreach (Guid member in new[] { alice, bob, carol })
        {
            await _presence.SeenAsync(SignedInWithKey(member));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        _directory.Remove(carol);

        (int total, IReadOnlyList<OnlineMember> page) = await _presence.PageAsync(0, 2);

        Assert.That(total, Is.EqualTo(2));
        Assert.That(page.Select(m => m.Name), Is.EqualTo(["Bob", "Alice"]));
        Assert.That(await _presence.CountOnlineAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Each_row_carries_the_members_avatar_or_none()
    {
        Guid alice = _directory.Add(1, "Alice", avatarUrl: "/media/alice.jpg?width=64&height=64");
        Guid bob = _directory.Add(2, "Bob");
        await _presence.SeenAsync(SignedInWithKey(alice));
        await _presence.SeenAsync(SignedInWithKey(bob));

        (_, IReadOnlyList<OnlineMember> page) = await _presence.PageAsync(0, 10);

        Assert.That(page.Single(m => m.Key == alice).AvatarUrl, Is.EqualTo("/media/alice.jpg?width=64&height=64"));
        Assert.That(page.Single(m => m.Key == bob).AvatarUrl, Is.Null);
    }

    private static ClaimsPrincipal SignedInWithKey(Guid key) =>
        new(new ClaimsIdentity([new Claim("sub", key.ToString())], "Test"));

    private static ClaimsPrincipal SignedInWithId(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Test"));
}
