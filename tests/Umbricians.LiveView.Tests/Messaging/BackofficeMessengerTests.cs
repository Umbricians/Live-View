using System.Security.Claims;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Tests.Messaging;

public class BackofficeMessengerTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryBackofficeUserDirectory _directory = new();
    private readonly RecordingServerEventRouter _router = new();
    private readonly BackofficePresence _presence;
    private readonly BackofficeMessenger _messenger;
    private readonly Guid _me;

    public BackofficeMessengerTests()
    {
        _presence = new BackofficePresence(_directory, new StaticOptionsMonitor<LiveViewOptions>(new LiveViewOptions()), _clock);
        _messenger = new BackofficeMessenger(_router, _presence, _directory, _clock);
        _me = _directory.Add("Me");
    }

    private BackofficeSender Me => new(_me, "Me");

    [Fact]
    public async Task Selected_users_are_each_notified_and_can_fetch_the_message()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [alice, bob]), Me));

        Assert.Equal(2, sent.Recipients);
        Assert.Equal([alice, bob], _router.Notified.Select(n => n.UserKey));
        Assert.All(_router.Notified, n => Assert.Equal(sent.Message.Key, n.Event.Key));
        Assert.Equal("Me", _messenger.Receive(sent.Message.Key, alice)?.SenderName);
    }

    [Fact]
    public async Task The_sender_is_never_sent_their_own_message()
    {
        Guid alice = _directory.Add("Alice");

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [_me, alice]), Me));

        Assert.Equal(1, sent.Recipients);
        Assert.Equal([alice], _router.Notified.Select(n => n.UserKey));
        Assert.Null(_messenger.Receive(sent.Message.Key, _me));
    }

    [Fact]
    public async Task Everyone_online_means_everyone_online_but_the_sender_when_it_is_sent()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        Guid notSelected = _directory.Add("Carol");
        _presence.Seen(SignedIn(_me));
        _presence.Seen(SignedIn(alice));
        BackofficeMessageRequest request = Request(userKeys: [notSelected], allOnline: true);

        _presence.Seen(SignedIn(bob));
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(request, Me));

        Assert.Equal(2, sent.Recipients);
        Assert.Equivalent(new[] { alice, bob }, _router.Notified.Select(n => n.UserKey).ToArray());
    }

    [Fact]
    public async Task The_all_online_count_matches_who_a_message_to_everyone_online_reaches()
    {
        _presence.Seen(SignedIn(_me));
        _presence.Seen(SignedIn(_directory.Add("Alice")));
        _presence.Seen(SignedIn(_directory.Add("Bob")));

        int counted = _messenger.CountAllOnlineRecipients(_me);
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(allOnline: true), Me));

        Assert.Equal(2, counted);
        Assert.Equal(counted, sent.Recipients);
    }

    [Fact]
    public void The_all_online_count_includes_everyone_when_the_sender_is_not_online()
    {
        _presence.Seen(SignedIn(_directory.Add("Alice")));

        Assert.Equal(1, _messenger.CountAllOnlineRecipients(_me));
        Assert.Equal(1, _messenger.CountAllOnlineRecipients(null));
    }

    [Fact]
    public async Task Selected_users_who_have_gone_offline_are_still_sent_to()
    {
        Guid alice = _directory.Add("Alice");
        _presence.Seen(SignedIn(alice));
        _clock.Advance(TimeSpan.FromHours(1));

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [alice]), Me));

        Assert.Equal(0, _presence.CountOnline());
        Assert.Equal([sent.Message.Key], _messenger.GetUndelivered(alice));
    }

    [Fact]
    public async Task A_user_selected_twice_is_sent_to_once()
    {
        Guid alice = _directory.Add("Alice");

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [alice, alice]), Me));

        Assert.Equal(1, sent.Recipients);
        Assert.Single(_router.Notified);
    }

    [Fact]
    public async Task The_headline_and_message_are_trimmed()
    {
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(
            Request(headline: "  Restart at 5  ", message: "\n Save your work. \n", userKeys: [_directory.Add("Alice")]), Me));

        Assert.Equal("Restart at 5", sent.Message.Headline);
        Assert.Equal("Save your work.", sent.Message.Message);
    }

    [Theory]
    [InlineData("warning", "warning")]
    [InlineData("danger", "danger")]
    [InlineData("purple", "default")]
    [InlineData("WARNING", "default")]
    [InlineData(null, "default")]
    public async Task Unknown_colours_fall_back_to_default(string? color, string expected)
    {
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(
            Request(color: color, userKeys: [_directory.Add("Alice")]), Me));

        Assert.Equal(expected, sent.Message.Color);
    }

    [Theory]
    [InlineData(200, 2000, true)]
    [InlineData(201, 0, false)]
    [InlineData(0, 0, false)]
    [InlineData(1, 2001, false)]
    public async Task Headlines_and_messages_must_fit_their_limits(int headlineLength, int messageLength, bool accepted)
    {
        BackofficeSendResult result = await _messenger.SendAsync(
            Request(headline: new string('h', headlineLength), message: new string('m', messageLength), userKeys: [_directory.Add("Alice")]),
            Me);

        Assert.Equal(accepted, result is BackofficeSendResult.Sent);
    }

    [Fact]
    public async Task A_headline_of_only_spaces_is_rejected()
    {
        BackofficeSendResult result = await _messenger.SendAsync(Request(headline: "   ", userKeys: [_directory.Add("Alice")]), Me);

        Assert.IsType<BackofficeSendResult.Rejected>(result);
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public async Task At_most_a_thousand_users_can_be_sent_to(int count, bool accepted)
    {
        Guid[] users = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

        BackofficeSendResult result = await _messenger.SendAsync(Request(userKeys: users), Me);

        Assert.Equal(accepted, result is BackofficeSendResult.Sent);
    }

    [Fact]
    public async Task A_message_with_nobody_but_the_sender_to_go_to_is_rejected_and_not_delivered()
    {
        _presence.Seen(SignedIn(_me));

        BackofficeSendResult selected = await _messenger.SendAsync(Request(userKeys: [_me]), Me);
        BackofficeSendResult allOnline = await _messenger.SendAsync(Request(allOnline: true), Me);
        BackofficeSendResult nobody = await _messenger.SendAsync(Request(), Me);
        BackofficeSendResult onlyMeInGroup = await _messenger.SendAsync(
            Request(userGroupKeys: [_directory.AddUserGroup(_me)]), Me);
        BackofficeSendResult noGroups = await _messenger.SendAsync(Request(userGroupKeys: []), Me);

        Assert.IsType<BackofficeSendResult.Rejected>(selected);
        Assert.IsType<BackofficeSendResult.Rejected>(allOnline);
        Assert.IsType<BackofficeSendResult.Rejected>(nobody);
        Assert.IsType<BackofficeSendResult.Rejected>(onlyMeInGroup);
        Assert.IsType<BackofficeSendResult.Rejected>(noGroups);
        Assert.Empty(_router.Notified);
        Assert.Empty(_messenger.GetUndelivered(_me));
    }

    [Fact]
    public async Task User_groups_mean_everyone_online_in_any_of_them_but_the_sender()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        Guid carol = _directory.Add("Carol");
        foreach (Guid user in new[] { _me, alice, bob, carol })
        {
            _presence.Seen(SignedIn(user));
        }

        Guid editors = _directory.AddUserGroup(_me, alice);
        Guid translators = _directory.AddUserGroup(alice, bob);

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userGroupKeys: [editors, translators]), Me));

        Assert.Equal(2, sent.Recipients);
        Assert.Equivalent(new[] { alice, bob }, _router.Notified.Select(n => n.UserKey).ToArray());
        Assert.Single(_router.Notified, n => n.UserKey == alice);
    }

    [Fact]
    public async Task Users_in_the_groups_who_are_offline_or_only_on_MCP_are_not_sent_to()
    {
        Guid alice = _directory.Add("Alice");
        Guid dave = _directory.Add("Dave");
        Guid erin = _directory.Add("Erin");
        _presence.Seen(SignedIn(dave));
        _clock.Advance(TimeSpan.FromHours(1));
        _presence.Seen(SignedIn(alice));
        _presence.McpSeen(erin);
        Guid group = _directory.AddUserGroup(alice, dave, erin);

        AssertSent(await _messenger.SendAsync(Request(userGroupKeys: [group]), Me));

        Assert.Equal([alice], _router.Notified.Select(n => n.UserKey));
    }

    [Fact]
    public async Task Users_in_the_groups_whose_account_has_gone_are_not_sent_to()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        _presence.Seen(SignedIn(alice));
        _presence.Seen(SignedIn(bob));
        Guid group = _directory.AddUserGroup(alice, bob);
        _directory.Remove(bob);

        AssertSent(await _messenger.SendAsync(Request(userGroupKeys: [group]), Me));

        Assert.Equal([alice], _router.Notified.Select(n => n.UserKey));
    }

    [Fact]
    public async Task The_user_group_count_matches_who_a_message_to_the_groups_reaches()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        foreach (Guid user in new[] { _me, alice, bob })
        {
            _presence.Seen(SignedIn(user));
        }

        Guid group = _directory.AddUserGroup(_me, alice, bob);

        (int counted, string? rejected) = await _messenger.CountUserGroupRecipientsAsync([group], _me);
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userGroupKeys: [group]), Me));

        Assert.Null(rejected);
        Assert.Equal(2, counted);
        Assert.Equal(counted, sent.Recipients);
    }

    [Fact]
    public async Task Unknown_user_groups_are_rejected()
    {
        Guid alice = _directory.Add("Alice");
        _presence.Seen(SignedIn(alice));
        Guid[] groups = [_directory.AddUserGroup(alice), Guid.NewGuid()];

        BackofficeSendResult result = await _messenger.SendAsync(Request(userGroupKeys: groups), Me);
        (_, string? rejected) = await _messenger.CountUserGroupRecipientsAsync(groups, _me);

        Assert.IsType<BackofficeSendResult.Rejected>(result);
        Assert.NotNull(rejected);
        Assert.Empty(_router.Notified);
    }

    [Fact]
    public async Task User_groups_take_the_place_of_selected_users()
    {
        Guid alice = _directory.Add("Alice");
        Guid carol = _directory.Add("Carol");
        _presence.Seen(SignedIn(alice));
        Guid group = _directory.AddUserGroup(alice);

        AssertSent(await _messenger.SendAsync(Request(userKeys: [carol], userGroupKeys: [group]), Me));

        Assert.Equal([alice], _router.Notified.Select(n => n.UserKey));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task At_most_a_hundred_user_groups_can_be_chosen(int count, bool accepted)
    {
        Guid alice = _directory.Add("Alice");
        _presence.Seen(SignedIn(alice));
        Guid[] groups = [_directory.AddUserGroup(alice), .. Enumerable.Range(1, count - 1).Select(_ => _directory.AddUserGroup())];

        BackofficeSendResult result = await _messenger.SendAsync(Request(userGroupKeys: groups), Me);

        Assert.Equal(accepted, result is BackofficeSendResult.Sent);
    }

    private static BackofficeMessageRequest Request(
        string? headline = "Heads up",
        string? message = "Restarting soon.",
        string? color = null,
        Guid[]? userKeys = null,
        Guid[]? userGroupKeys = null,
        bool allOnline = false) =>
        new(headline, message, color, userKeys, userGroupKeys, allOnline);

    private static BackofficeSendResult.Sent AssertSent(BackofficeSendResult result) =>
        Assert.IsType<BackofficeSendResult.Sent>(result);

    private static ClaimsPrincipal SignedIn(Guid key) =>
        new(new ClaimsIdentity([new Claim("sub", key.ToString())], "Test"));

    private sealed class RecordingServerEventRouter : IServerEventRouter
    {
        public List<(ServerEvent Event, Guid UserKey)> Notified { get; } = [];

        public Task NotifyUserAsync(ServerEvent serverEvent, Guid userKey)
        {
            Notified.Add((serverEvent, userKey));
            return Task.CompletedTask;
        }

        public Task RouteEventAsync(ServerEvent serverEvent) => Task.CompletedTask;

        public Task RouteEventAsync(ServerEvent serverEvent, ServerEventRoutingContext routingContext) => Task.CompletedTask;

        public Task BroadcastEventAsync(ServerEvent serverEvent) => Task.CompletedTask;
    }
}
