using System.Security.Claims;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Tests.Messaging;

public class BackofficeMessengerTests
{
    private LiveViewOptions _options;
    private FakeTimeProvider _clock;
    private InMemoryBackofficeUserDirectory _directory;
    private RecordingServerEventRouter _router;
    private RecordingEventAggregator _events;
    private BackofficePresence _presence;
    private BackofficeMessenger _messenger;
    private Guid _me;

    [SetUp]
    public void SetUp()
    {
        _options = new LiveViewOptions();
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        _directory = new InMemoryBackofficeUserDirectory();
        _router = new RecordingServerEventRouter();
        _events = new RecordingEventAggregator();
        var options = new StaticOptionsMonitor<LiveViewOptions>(_options);
        _presence = new BackofficePresence(new InMemoryActivityStore(options), _directory, options, _clock);
        _messenger = new BackofficeMessenger(_router, _presence, _directory, new InMemoryBackofficeMessageStore(_clock), _events, options, _clock);
        _me = _directory.Add("Me");
    }

    private BackofficeSender Me => new(_me, "Me", CanChooseUserGroups: true);

    [Test]
    public async Task Selected_users_are_each_notified_and_can_fetch_the_message()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [alice, bob]), Me));

        Assert.That(sent.Recipients, Is.EqualTo(2));
        Assert.That(_router.Notified.Select(n => n.UserKey), Is.EqualTo([alice, bob]));
        Assert.That(_router.Notified.Select(n => n.Event.Key), Is.All.EqualTo(sent.Message.Key));
        Assert.That((await _messenger.ReceiveAsync(sent.Message.Key, alice))?.SenderName, Is.EqualTo("Me"));
    }

    [Test]
    public async Task The_sender_is_never_sent_their_own_message()
    {
        Guid alice = _directory.Add("Alice");

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [_me, alice]), Me));

        Assert.That(sent.Recipients, Is.EqualTo(1));
        Assert.That(_router.Notified.Select(n => n.UserKey), Is.EqualTo([alice]));
        Assert.That(await _messenger.ReceiveAsync(sent.Message.Key, _me), Is.Null);
    }

    [Test]
    public async Task Everyone_online_means_everyone_online_but_the_sender_when_it_is_sent()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        Guid notSelected = _directory.Add("Carol");
        await _presence.SeenAsync(SignedIn(_me));
        await _presence.SeenAsync(SignedIn(alice));
        BackofficeMessageRequest request = Request(userKeys: [notSelected], allOnline: true);

        await _presence.SeenAsync(SignedIn(bob));
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(request, Me));

        Assert.That(sent.Recipients, Is.EqualTo(2));
        Assert.That(_router.Notified.Select(n => n.UserKey).ToArray(), Is.SupersetOf(new[] { alice, bob }));
    }

    [Test]
    public async Task The_all_online_count_matches_who_a_message_to_everyone_online_reaches()
    {
        await _presence.SeenAsync(SignedIn(_me));
        await _presence.SeenAsync(SignedIn(_directory.Add("Alice")));
        await _presence.SeenAsync(SignedIn(_directory.Add("Bob")));

        int counted = await _messenger.CountAllOnlineRecipientsAsync(_me);
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(allOnline: true), Me));

        Assert.That(counted, Is.EqualTo(2));
        Assert.That(sent.Recipients, Is.EqualTo(counted));
    }

    [Test]
    public async Task The_all_online_count_includes_everyone_when_the_sender_is_not_online()
    {
        await _presence.SeenAsync(SignedIn(_directory.Add("Alice")));

        Assert.That(await _messenger.CountAllOnlineRecipientsAsync(_me), Is.EqualTo(1));
        Assert.That(await _messenger.CountAllOnlineRecipientsAsync(null), Is.EqualTo(1));
    }

    [Test]
    public async Task Selected_users_who_have_gone_offline_are_still_sent_to()
    {
        Guid alice = _directory.Add("Alice");
        await _presence.SeenAsync(SignedIn(alice));
        _clock.Advance(TimeSpan.FromHours(1));

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [alice]), Me));

        Assert.That(await _presence.CountOnlineAsync(), Is.Zero);
        Assert.That(await _messenger.GetUndeliveredAsync(alice), Is.EqualTo([sent.Message.Key]));
    }

    [Test]
    public async Task A_message_is_kept_for_the_configured_lifetime_then_discarded()
    {
        _options.BackofficeMessageLifetimeMinutes = 5;
        Guid alice = _directory.Add("Alice");
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [alice]), Me));

        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.That(await _messenger.GetUndeliveredAsync(alice), Is.EqualTo([sent.Message.Key]));

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.That(await _messenger.GetUndeliveredAsync(alice), Is.Empty);
    }

    [Test]
    public async Task Messages_are_kept_for_fifteen_minutes_by_default()
    {
        Guid alice = _directory.Add("Alice");
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [alice]), Me));

        _clock.Advance(TimeSpan.FromMinutes(14));
        Assert.That(await _messenger.GetUndeliveredAsync(alice), Is.EqualTo([sent.Message.Key]));

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.That(await _messenger.GetUndeliveredAsync(alice), Is.Empty);
    }

    [Test]
    public async Task A_user_selected_twice_is_sent_to_once()
    {
        Guid alice = _directory.Add("Alice");

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [alice, alice]), Me));

        Assert.That(sent.Recipients, Is.EqualTo(1));
        Assert.That(_router.Notified, Has.Exactly(1).Items);
    }

    [Test]
    public async Task The_headline_and_message_are_trimmed()
    {
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(
            Request(headline: "  Restart at 5  ", message: "\n Save your work. \n", userKeys: [_directory.Add("Alice")]), Me));

        Assert.That(sent.Message.Headline, Is.EqualTo("Restart at 5"));
        Assert.That(sent.Message.Message, Is.EqualTo("Save your work."));
    }

    [TestCase("warning", "warning")]
    [TestCase("danger", "danger")]
    [TestCase("purple", "default")]
    [TestCase("WARNING", "default")]
    [TestCase(null, "default")]
    public async Task Unknown_colours_fall_back_to_default(string? color, string expected)
    {
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(
            Request(color: color, userKeys: [_directory.Add("Alice")]), Me));

        Assert.That(sent.Message.Color, Is.EqualTo(expected));
    }

    [TestCase(200, 2000, true)]
    [TestCase(201, 0, false)]
    [TestCase(0, 0, false)]
    [TestCase(1, 2001, false)]
    public async Task Headlines_and_messages_must_fit_their_limits(int headlineLength, int messageLength, bool accepted)
    {
        BackofficeSendResult result = await _messenger.SendAsync(
            Request(headline: new string('h', headlineLength), message: new string('m', messageLength), userKeys: [_directory.Add("Alice")]),
            Me);

        Assert.That(result is BackofficeSendResult.Sent, Is.EqualTo(accepted));
    }

    [Test]
    public async Task A_headline_of_only_spaces_is_rejected()
    {
        BackofficeSendResult result = await _messenger.SendAsync(Request(headline: "   ", userKeys: [_directory.Add("Alice")]), Me);

        Assert.That(result, Is.TypeOf<BackofficeSendResult.Rejected>());
    }

    [TestCase(1000, true)]
    [TestCase(1001, false)]
    public async Task At_most_a_thousand_users_can_be_sent_to(int count, bool accepted)
    {
        Guid[] users = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

        BackofficeSendResult result = await _messenger.SendAsync(Request(userKeys: users), Me);

        Assert.That(result is BackofficeSendResult.Sent, Is.EqualTo(accepted));
    }

    [Test]
    public async Task A_message_with_nobody_but_the_sender_to_go_to_is_rejected_and_not_delivered()
    {
        await _presence.SeenAsync(SignedIn(_me));

        BackofficeSendResult selected = await _messenger.SendAsync(Request(userKeys: [_me]), Me);
        BackofficeSendResult allOnline = await _messenger.SendAsync(Request(allOnline: true), Me);
        BackofficeSendResult nobody = await _messenger.SendAsync(Request(), Me);
        BackofficeSendResult onlyMeInGroup = await _messenger.SendAsync(
            Request(userGroupKeys: [_directory.AddUserGroup(_me)]), Me);
        BackofficeSendResult noGroups = await _messenger.SendAsync(Request(userGroupKeys: []), Me);

        Assert.That(selected, Is.TypeOf<BackofficeSendResult.Rejected>());
        Assert.That(allOnline, Is.TypeOf<BackofficeSendResult.Rejected>());
        Assert.That(nobody, Is.TypeOf<BackofficeSendResult.Rejected>());
        Assert.That(onlyMeInGroup, Is.TypeOf<BackofficeSendResult.Rejected>());
        Assert.That(noGroups, Is.TypeOf<BackofficeSendResult.Rejected>());
        Assert.That(_router.Notified, Is.Empty);
        Assert.That(await _messenger.GetUndeliveredAsync(_me), Is.Empty);
    }

    [Test]
    public async Task A_handler_can_stop_a_message_and_the_reason_comes_back()
    {
        Guid alice = _directory.Add("Alice");
        _events.OnPublish = n =>
        {
            if (n is BackofficeMessageSendingNotification sending)
            {
                sending.CancelOperation(new EventMessage("Policy", "Not during the release freeze.", EventMessageType.Warning));
            }
        };

        BackofficeSendResult result = await _messenger.SendAsync(Request(userKeys: [alice]), Me);

        Assert.That(result, Is.TypeOf<BackofficeSendResult.Rejected>().With.Property(nameof(BackofficeSendResult.Rejected.Reason)).EqualTo("Not during the release freeze."));
        Assert.That(_router.Notified, Is.Empty);
        Assert.That(await _messenger.GetUndeliveredAsync(alice), Is.Empty);
        Assert.That(_events.Published.OfType<BackofficeMessageSentNotification>(), Is.Empty);
    }

    [Test]
    public async Task A_handler_that_cancels_without_a_reason_still_says_why()
    {
        _events.OnPublish = n =>
        {
            if (n is BackofficeMessageSendingNotification sending)
            {
                sending.Cancel = true;
            }
        };

        BackofficeSendResult result = await _messenger.SendAsync(Request(userKeys: [_directory.Add("Alice")]), Me);

        Assert.That(result, Is.TypeOf<BackofficeSendResult.Rejected>().With.Property(nameof(BackofficeSendResult.Rejected.Reason)).EqualTo(CancelledSend.DefaultReason));
    }

    [Test]
    public async Task Sending_comes_before_delivery_with_the_message_its_recipients_and_its_sender()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        _events.OnPublish = n =>
        {
            if (n is BackofficeMessageSendingNotification)
            {
                Assert.That(_router.Notified, Is.Empty);
            }
        };

        await _messenger.SendAsync(Request(headline: "  Heads up  ", userKeys: [alice, bob]), Me);

        var sending = _events.Single<BackofficeMessageSendingNotification>();
        Assert.That(sending.RecipientKeys, Is.EqualTo([alice, bob]));
        Assert.That(sending.SenderKey, Is.EqualTo(_me));
        Assert.That(sending.Message.Headline, Is.EqualTo("Heads up"));
    }

    [Test]
    public async Task Sent_comes_after_delivery_with_the_recipients()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userKeys: [alice, bob]), Me));

        Assert.That(_events.Published.Last(), Is.TypeOf<BackofficeMessageSentNotification>());
        var notification = (BackofficeMessageSentNotification)_events.Published.Last();
        Assert.That(notification.Message.Key, Is.EqualTo(sent.Message.Key));
        Assert.That(notification.RecipientKeys, Is.EqualTo([alice, bob]));
        Assert.That(notification.SenderKey, Is.EqualTo(_me));
        Assert.That(_router.Notified, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task A_message_that_is_rejected_publishes_nothing()
    {
        await _messenger.SendAsync(Request(headline: "   ", userKeys: [_directory.Add("Alice")]), Me);

        Assert.That(_events.Published, Is.Empty);
    }

    [Test]
    public async Task User_groups_mean_everyone_online_in_any_of_them_but_the_sender()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        Guid carol = _directory.Add("Carol");
        foreach (Guid user in new[] { _me, alice, bob, carol })
        {
            await _presence.SeenAsync(SignedIn(user));
        }

        Guid editors = _directory.AddUserGroup(_me, alice);
        Guid translators = _directory.AddUserGroup(alice, bob);

        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userGroupKeys: [editors, translators]), Me));

        Assert.That(sent.Recipients, Is.EqualTo(2));
        Assert.That(_router.Notified.Select(n => n.UserKey).ToArray(), Is.SupersetOf(new[] { alice, bob }));
        Assert.That(_router.Notified.Select(n => n.UserKey), Has.Exactly(1).EqualTo(alice));
    }

    [Test]
    public async Task The_sending_notification_for_a_user_group_send_sees_the_final_recipients()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        foreach (Guid user in new[] { _me, alice, bob })
        {
            await _presence.SeenAsync(SignedIn(user));
        }

        Guid group = _directory.AddUserGroup(_me, alice, bob);

        await _messenger.SendAsync(Request(userGroupKeys: [group]), Me);

        var sending = _events.Single<BackofficeMessageSendingNotification>();
        Assert.That(sending.RecipientKeys.ToArray(), Is.SupersetOf(new[] { alice, bob }));
        Assert.That(sending.RecipientKeys, Does.Not.Contain(_me));
    }

    [Test]
    public async Task Users_in_the_groups_who_are_offline_or_only_on_MCP_are_not_sent_to()
    {
        Guid alice = _directory.Add("Alice");
        Guid dave = _directory.Add("Dave");
        Guid erin = _directory.Add("Erin");
        await _presence.SeenAsync(SignedIn(dave));
        _clock.Advance(TimeSpan.FromHours(1));
        await _presence.SeenAsync(SignedIn(alice));
        await _presence.McpSeenAsync(erin);
        Guid group = _directory.AddUserGroup(alice, dave, erin);

        AssertSent(await _messenger.SendAsync(Request(userGroupKeys: [group]), Me));

        Assert.That(_router.Notified.Select(n => n.UserKey), Is.EqualTo([alice]));
    }

    [Test]
    public async Task Users_in_the_groups_whose_account_has_gone_are_not_sent_to()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        await _presence.SeenAsync(SignedIn(alice));
        await _presence.SeenAsync(SignedIn(bob));
        Guid group = _directory.AddUserGroup(alice, bob);
        _directory.Remove(bob);

        AssertSent(await _messenger.SendAsync(Request(userGroupKeys: [group]), Me));

        Assert.That(_router.Notified.Select(n => n.UserKey), Is.EqualTo([alice]));
    }

    [Test]
    public async Task The_user_group_count_matches_who_a_message_to_the_groups_reaches()
    {
        Guid alice = _directory.Add("Alice");
        Guid bob = _directory.Add("Bob");
        foreach (Guid user in new[] { _me, alice, bob })
        {
            await _presence.SeenAsync(SignedIn(user));
        }

        Guid group = _directory.AddUserGroup(_me, alice, bob);

        (int counted, string? rejected) = await _messenger.CountUserGroupRecipientsAsync([group], Me);
        BackofficeSendResult.Sent sent = AssertSent(await _messenger.SendAsync(Request(userGroupKeys: [group]), Me));

        Assert.That(rejected, Is.Null);
        Assert.That(counted, Is.EqualTo(2));
        Assert.That(sent.Recipients, Is.EqualTo(counted));
    }

    [Test]
    public async Task Unknown_user_groups_are_rejected()
    {
        Guid alice = _directory.Add("Alice");
        await _presence.SeenAsync(SignedIn(alice));
        Guid[] groups = [_directory.AddUserGroup(alice), Guid.NewGuid()];

        BackofficeSendResult result = await _messenger.SendAsync(Request(userGroupKeys: groups), Me);
        (_, string? rejected) = await _messenger.CountUserGroupRecipientsAsync(groups, Me);

        Assert.That(result, Is.TypeOf<BackofficeSendResult.Rejected>());
        Assert.That(rejected, Is.Not.Null);
        Assert.That(_router.Notified, Is.Empty);
    }

    [Test]
    public async Task User_groups_take_the_place_of_selected_users()
    {
        Guid alice = _directory.Add("Alice");
        Guid carol = _directory.Add("Carol");
        await _presence.SeenAsync(SignedIn(alice));
        Guid group = _directory.AddUserGroup(alice);

        AssertSent(await _messenger.SendAsync(Request(userKeys: [carol], userGroupKeys: [group]), Me));

        Assert.That(_router.Notified.Select(n => n.UserKey), Is.EqualTo([alice]));
    }

    [TestCase(100, true)]
    [TestCase(101, false)]
    public async Task At_most_a_hundred_user_groups_can_be_chosen(int count, bool accepted)
    {
        Guid alice = _directory.Add("Alice");
        await _presence.SeenAsync(SignedIn(alice));
        Guid[] groups = [_directory.AddUserGroup(alice), .. Enumerable.Range(1, count - 1).Select(_ => _directory.AddUserGroup())];

        BackofficeSendResult result = await _messenger.SendAsync(Request(userGroupKeys: groups), Me);

        Assert.That(result is BackofficeSendResult.Sent, Is.EqualTo(accepted));
    }

    [Test]
    public async Task User_groups_need_access_to_the_Users_section()
    {
        Guid alice = _directory.Add("Alice");
        await _presence.SeenAsync(SignedIn(alice));
        Guid group = _directory.AddUserGroup(alice);
        var sender = new BackofficeSender(_me, "Me", CanChooseUserGroups: false);

        BackofficeSendResult result = await _messenger.SendAsync(Request(userGroupKeys: [group]), sender);
        (int counted, string? rejected) = await _messenger.CountUserGroupRecipientsAsync([group], sender);

        Assert.That(result, Is.TypeOf<BackofficeSendResult.Rejected>().With.Property(nameof(BackofficeSendResult.Rejected.Reason)).EqualTo(BackofficeMessenger.UserGroupsNeedUsersSection));
        Assert.That(_router.Notified, Is.Empty);
        Assert.That(counted, Is.Zero);
        Assert.That(rejected, Is.EqualTo(BackofficeMessenger.UserGroupsNeedUsersSection));
    }

    [Test]
    public async Task Selected_users_and_everyone_online_do_not_need_the_Users_section()
    {
        Guid alice = _directory.Add("Alice");
        await _presence.SeenAsync(SignedIn(alice));
        Guid group = _directory.AddUserGroup(alice);
        var sender = new BackofficeSender(_me, "Me", CanChooseUserGroups: false);

        AssertSent(await _messenger.SendAsync(Request(userKeys: [alice]), sender));
        AssertSent(await _messenger.SendAsync(Request(allOnline: true), sender));
        AssertSent(await _messenger.SendAsync(Request(allOnline: true, userGroupKeys: [group]), sender));
    }

    private static BackofficeMessageRequest Request(
        string? headline = "Heads up",
        string? message = "Restarting soon.",
        string? color = null,
        Guid[]? userKeys = null,
        Guid[]? userGroupKeys = null,
        bool allOnline = false) =>
        new(headline, message, color, userKeys, userGroupKeys, allOnline);

    private static BackofficeSendResult.Sent AssertSent(BackofficeSendResult result)
    {
        Assert.That(result, Is.TypeOf<BackofficeSendResult.Sent>());
        return (BackofficeSendResult.Sent)result;
    }

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
