using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core.Events;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Tests.Messaging;

/// <summary>
/// The member send rules: what a message must say, who it can go to, how it is delivered and how long it is shown.
/// </summary>
public class MemberMessagingServiceTests
{
    private FakeTimeProvider _clock;
    private InMemoryMemberDirectory _directory;
    private RecordingSender _banner;
    private RecordingEventAggregator _events;
    private Guid _meKey;
    private MemberPresence _presence;
    private MemberMessagingService _messaging;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        _directory = new InMemoryMemberDirectory();
        _banner = new RecordingSender("banner");
        _events = new RecordingEventAggregator();
        _meKey = Guid.NewGuid();
        var options = new StaticOptionsMonitor<LiveViewOptions>(new LiveViewOptions());
        _presence = new MemberPresence(new InMemoryActivityStore(options), _directory, options, _clock);
        _messaging = new MemberMessagingService([_banner], _presence, _events, _clock, NullLogger<MemberMessagingService>.Instance);
    }

    [Test]
    public async Task A_message_is_trimmed_and_its_colour_becomes_its_type()
    {
        await SendAsync(Request(headline: "  Maintenance  ", message: " Tonight ", color: "danger"));

        Assert.That(_banner.Sent, Has.Exactly(1).Items);
        MemberMessage sent = _banner.Sent.Single();
        Assert.That(sent.Headline, Is.EqualTo("Maintenance"));
        Assert.That(sent.Body, Is.EqualTo("Tonight"));
        Assert.That(sent.Type, Is.EqualTo(MemberMessageType.Urgent));
    }

    [TestCase(200, 2000, true)]
    [TestCase(201, 0, false)]
    [TestCase(0, 0, false)]
    [TestCase(1, 2001, false)]
    public async Task Headlines_and_messages_must_fit_the_same_limits_as_backoffice_messages(
        int headlineLength, int messageLength, bool accepted)
    {
        MemberSendResult result = await SendAsync(
            Request(headline: new string('h', headlineLength), message: new string('m', messageLength)));

        Assert.That(result is MemberSendResult.Sent, Is.EqualTo(accepted));
    }

    [Test]
    public async Task All_online_means_the_members_online_when_it_is_sent()
    {
        Guid ann = _directory.Add(1, "Ann");
        Guid bob = _directory.Add(2, "Bob");
        await _presence.McpSeenAsync(ann);
        await _presence.McpSeenAsync(bob);

        MemberSendResult.Sent sent = AssertSent(await SendAsync(Request(audience: "allOnline")));

        Assert.That(sent.Result.Recipients, Is.EqualTo(2));
        Assert.That(_banner.Sent, Has.Exactly(1).Items);
        Assert.That(_banner.Sent.Single().MemberKeys.Order(), Is.EqualTo(new[] { ann, bob }.Order()));
    }

    [TestCase("everyone")]
    [TestCase(null)]
    public async Task An_unknown_audience_is_rejected(string? audience)
    {
        Assert.That(await SendAsync(Request(audience: audience)), Is.TypeOf<MemberSendResult.Rejected>());
        Assert.That(_banner.Sent, Is.Empty);
    }

    [Test]
    public async Task Selected_needs_someone_selected()
    {
        Assert.That(await SendAsync(Request(audience: "selected", memberKeys: [])), Is.TypeOf<MemberSendResult.Rejected>());
    }

    [Test]
    public async Task A_member_selected_twice_is_sent_to_once()
    {
        Guid ann = Guid.NewGuid();

        MemberSendResult.Sent sent = AssertSent(await SendAsync(Request(audience: "selected", memberKeys: [ann, ann])));

        Assert.That(sent.Result.Recipients, Is.EqualTo(1));
    }

    [Test]
    public async Task Only_delivery_methods_the_site_has_count()
    {
        Assert.That(await SendAsync(Request(senders: ["email"])), Is.TypeOf<MemberSendResult.Rejected>());
        Assert.That(await SendAsync(Request(senders: ["BANNER"])), Is.TypeOf<MemberSendResult.Sent>());
    }

    [Test]
    public async Task A_message_must_stop_being_shown_in_the_future()
    {
        Assert.That(await SendAsync(Request() with { ExpiresAt = _clock.GetUtcNow() }), Is.TypeOf<MemberSendResult.Rejected>());
        Assert.That(await SendAsync(Request() with { ExpiresAt = null }), Is.TypeOf<MemberSendResult.Rejected>());
        Assert.That(_banner.Sent, Is.Empty);
    }

    [Test]
    public async Task A_handler_can_stop_a_message_and_the_reason_comes_back()
    {
        _events.OnPublish = n =>
        {
            if (n is MemberMessageSendingNotification sending)
            {
                sending.CancelOperation(new EventMessage("Policy", "Not during the release freeze.", EventMessageType.Warning));
            }
        };

        MemberSendResult result = await SendAsync(Request());

        Assert.That(result, Is.TypeOf<MemberSendResult.Rejected>().With.Property(nameof(MemberSendResult.Rejected.Reason)).EqualTo("Not during the release freeze."));
        Assert.That(_banner.Sent, Is.Empty);
        Assert.That(_events.Published.OfType<MemberMessageSentNotification>(), Is.Empty);
    }

    [Test]
    public async Task A_handler_that_cancels_without_a_reason_still_says_why()
    {
        _events.OnPublish = n =>
        {
            if (n is MemberMessageSendingNotification sending)
            {
                sending.Cancel = true;
            }
        };

        MemberSendResult result = await SendAsync(Request());

        Assert.That(result, Is.TypeOf<MemberSendResult.Rejected>().With.Property(nameof(MemberSendResult.Rejected.Reason)).EqualTo(CancelledSend.DefaultReason));
    }

    [Test]
    public async Task Sending_comes_before_delivery_with_the_message_its_delivery_methods_and_its_sender()
    {
        Guid ann = _directory.Add(1, "Ann");
        await _presence.McpSeenAsync(ann);
        _events.OnPublish = n =>
        {
            if (n is MemberMessageSendingNotification)
            {
                Assert.That(_banner.Sent, Is.Empty);
            }
        };

        await SendAsync(Request(audience: "allOnline"));

        var sending = _events.Single<MemberMessageSendingNotification>();
        Assert.That(sending.DeliveryMethods, Is.EqualTo(["banner"]));
        Assert.That(sending.SenderKey, Is.EqualTo(_meKey));
        Assert.That(sending.Message.MemberKeys, Is.EqualTo([ann]));
    }

    [Test]
    public async Task Sent_comes_after_delivery_with_what_each_delivery_method_did()
    {
        MemberSendResult.Sent sent = AssertSent(await SendAsync(Request()));

        Assert.That(_events.Published.Last(), Is.TypeOf<MemberMessageSentNotification>());
        var notification = (MemberMessageSentNotification)_events.Published.Last();
        Assert.That(notification.Message.Key, Is.EqualTo(sent.Result.Key));
        Assert.That(notification.SenderKey, Is.EqualTo(_meKey));
        Assert.That(notification.Results, Has.Exactly(1).Items);
        MemberMessageSenderResult result = notification.Results.Single();
        Assert.That(result.Alias, Is.EqualTo("banner"));
        Assert.That(result.Delivered, Is.EqualTo(sent.Result.Results[0].Delivered));
    }

    [Test]
    public async Task An_invalid_message_publishes_nothing()
    {
        await SendAsync(Request(headline: "   "));

        Assert.That(_events.Published, Is.Empty);
    }

    private static MemberSendResult.Sent AssertSent(MemberSendResult result)
    {
        Assert.That(result, Is.TypeOf<MemberSendResult.Sent>());
        return (MemberSendResult.Sent)result;
    }

    private Task<MemberSendResult> SendAsync(MemberMessageRequest request) =>
        _messaging.SendAsync(request, new BackofficeSender(_meKey, "Me"), CancellationToken.None);

    private MemberMessageRequest Request(
        string? headline = "Hello",
        string? message = "",
        string? color = "warning",
        string? audience = "allMembers",
        Guid[]? memberKeys = null,
        string[]? senders = null) =>
        new(headline, message, color, audience, memberKeys, senders ?? ["banner"], _clock.GetUtcNow().AddHours(1));

    private sealed class RecordingSender(string alias) : IMemberMessageSender
    {
        public List<MemberMessage> Sent { get; } = [];

        public string Alias => alias;

        public string Name => alias;

        public Task<MemberMessageResult> SendAsync(MemberMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.FromResult(new MemberMessageResult(message.MemberKeys.Count));
        }
    }
}
