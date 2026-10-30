using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Tests.Presence;

public class MemberPresenceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryMemberDirectory _directory = new();
    private readonly LiveViewOptions _options = new();
    private readonly MemberPresence _presence;

    public MemberPresenceTests() =>
        _presence = new MemberPresence(_directory, new StaticOptionsMonitor<LiveViewOptions>(_options), _clock);

    [Fact]
    public void A_member_seen_by_key_is_online()
    {
        Guid alice = _directory.Add(1, "Alice");

        _presence.Seen(SignedInWithKey(alice));

        Assert.Equal(1, _presence.CountOnline());
        Assert.Equal([alice], _presence.AllOnline());
    }

    [Fact]
    public void A_member_seen_by_numeric_id_is_held_by_their_key()
    {
        Guid alice = _directory.Add(42, "Alice");

        _presence.Seen(SignedInWithId("42"));
        _presence.Seen(SignedInWithKey(alice));

        Assert.Equal([alice], _presence.AllOnline());
    }

    [Fact]
    public void Visitors_who_are_not_signed_in_or_not_members_are_ignored()
    {
        _presence.Seen(new ClaimsPrincipal(new ClaimsIdentity()));
        _presence.Seen(SignedInWithId("999"));
        _presence.Seen(SignedInWithId("not-an-id"));

        Assert.Equal(0, _presence.CountOnline());
    }

    [Fact]
    public void A_member_goes_offline_once_the_activity_window_passes()
    {
        Guid alice = _directory.Add(1, "Alice");
        _presence.Seen(SignedInWithKey(alice));

        _clock.Advance(TimeSpan.FromSeconds(299));
        Assert.Equal(1, _presence.CountOnline());

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, _presence.CountOnline());
    }

    [Fact]
    public void The_members_channel_can_set_its_own_window()
    {
        _options.Channels[Constants.Channels.Members] = new LiveViewChannelOptions { ActivityWindowSeconds = 30 };
        Guid alice = _directory.Add(1, "Alice");
        _presence.Seen(SignedInWithKey(alice));

        _clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Equal(0, _presence.CountOnline());
    }

    [Fact]
    public void Signing_out_takes_a_member_offline_and_their_sign_out_request_does_not_bring_them_back()
    {
        Guid alice = _directory.Add(1, "Alice");
        _presence.Seen(SignedInWithKey(alice));

        _presence.SignedOut(alice);
        _presence.Seen(SignedInWithKey(alice));

        Assert.Equal(0, _presence.CountOnline());
    }

    [Fact]
    public void A_member_who_signs_back_in_after_the_marker_expires_is_online_again()
    {
        Guid alice = _directory.Add(1, "Alice");
        _presence.SignedOut(alice);

        _clock.Advance(MemberPresence.SignedOutMarkerLifetime);
        _presence.Seen(SignedInWithKey(alice));

        Assert.Equal(1, _presence.CountOnline());
    }

    [Fact]
    public void A_forgotten_member_is_offline()
    {
        Guid alice = _directory.Add(1, "Alice");
        _presence.Seen(SignedInWithKey(alice));

        _presence.Forget(alice);

        Assert.Equal(0, _presence.CountOnline());
    }

    [Fact]
    public async Task Pages_list_newest_arrivals_first_and_keep_their_order_as_members_browse()
    {
        Guid alice = _directory.Add(1, "Alice");
        Guid bob = _directory.Add(2, "Bob");
        Guid carol = _directory.Add(3, "Carol");
        foreach (Guid member in new[] { alice, bob, carol })
        {
            _presence.Seen(SignedInWithKey(member));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        _presence.Seen(SignedInWithKey(alice));

        (int total, IReadOnlyList<OnlineMember> first) = await _presence.PageAsync(0, 2);
        (_, IReadOnlyList<OnlineMember> second) = await _presence.PageAsync(2, 2);

        Assert.Equal(3, total);
        Assert.Equal(["Carol", "Bob"], first.Select(m => m.Name));
        Assert.Equal(["Alice"], second.Select(m => m.Name));
        Assert.Equal(_clock.GetUtcNow(), second[0].LastSeen);
    }

    [Fact]
    public async Task Rows_carry_the_members_details()
    {
        Guid alice = _directory.Add(1, "Alice", "alice@example.test", "Member");
        _presence.Seen(SignedInWithKey(alice));

        (_, IReadOnlyList<OnlineMember> page) = await _presence.PageAsync(0, 10);

        OnlineMember row = Assert.Single(page);
        Assert.Equal((alice, "Alice", "alice@example.test", "Member"), (row.Key, row.Name, row.Email, row.MemberType));
    }

    [Fact]
    public async Task Members_whose_account_has_gone_are_left_out_of_the_page_and_the_total()
    {
        Guid alice = _directory.Add(1, "Alice");
        Guid bob = _directory.Add(2, "Bob");
        Guid carol = _directory.Add(3, "Carol");
        foreach (Guid member in new[] { alice, bob, carol })
        {
            _presence.Seen(SignedInWithKey(member));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        _directory.Remove(carol);

        (int total, IReadOnlyList<OnlineMember> page) = await _presence.PageAsync(0, 2);

        Assert.Equal(2, total);
        Assert.Equal(["Bob", "Alice"], page.Select(m => m.Name));
        Assert.Equal(2, _presence.CountOnline());
    }

    private static ClaimsPrincipal SignedInWithKey(Guid key) =>
        new(new ClaimsIdentity([new Claim("sub", key.ToString())], "Test"));

    private static ClaimsPrincipal SignedInWithId(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Test"));

    private sealed class InMemoryMemberDirectory : IMemberDirectory
    {
        private readonly Dictionary<int, MemberDetails> _members = new();

        public Guid Add(int id, string name, string? email = null, string? memberType = null)
        {
            var key = Guid.NewGuid();
            _members[id] = new MemberDetails(key, name, email, memberType);
            return key;
        }

        public void Remove(Guid key) => _members.Remove(_members.Single(m => m.Value.Key == key).Key);

        public Guid? FindKey(int memberId) => _members.TryGetValue(memberId, out MemberDetails? member) ? member.Key : null;

        public Task<IReadOnlyList<MemberDetails>> GetAsync(IReadOnlyCollection<Guid> keys) =>
            Task.FromResult<IReadOnlyList<MemberDetails>>(_members.Values.Where(m => keys.Contains(m.Key)).ToList());
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
