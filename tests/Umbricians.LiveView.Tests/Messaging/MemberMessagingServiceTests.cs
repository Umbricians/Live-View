using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core.Events;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Tests.Messaging;

/// <summary>
/// The member send rules: what a message must say, who it can go to, how it is delivered and how long it is shown.
/// </summary>
public class MemberMessagingServiceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryMemberDirectory _directory = new();
    private readonly RecordingSender _banner = new("banner");
    private readonly RecordingEventAggregator _events = new();
    private readonly Guid _meKey = Guid.NewGuid();
    private readonly MemberPresence _presence;
    private readonly MemberMessagingService _messaging;

    public MemberMessagingServiceTests()
    {
        _presence = new MemberPresence(_directory, new StaticOptionsMonitor<LiveViewOptions>(new LiveViewOptions()), _clock);
        _messaging = new MemberMessagingService([_banner], _presence, _events, _clock, NullLogger<MemberMessagingService>.Instance);
    }

    [Fact]
    public async Task A_message_is_trimmed_and_its_colour_becomes_its_type()
    {
        await SendAsync(Request(headline: "  Maintenance  ", message: " Tonight ", color: "danger"));

        MemberMessage sent = Assert.Single(_banner.Sent);
        Assert.Equal("Maintenance", sent.Headline);
        Assert.Equal("Tonight", sent.Body);
        Assert.Equal(MemberMessageType.Urgent, sent.Type);
    }

    [Theory]
    [InlineData(200, 2000, true)]
    [InlineData(201, 0, false)]
    [InlineData(0, 0, false)]
    [InlineData(1, 2001, false)]
    public async Task Headlines_and_messages_must_fit_the_same_limits_as_backoffice_messages(
        int headlineLength, int messageLength, bool accepted)
    {
        MemberSendResult result = await SendAsync(
            Request(headline: new string('h', headlineLength), message: new string('m', messageLength)));

        Assert.Equal(accepted, result is MemberSendResult.Sent);
    }

    [Fact]
    public async Task All_online_means_the_members_online_when_it_is_sent()
    {
        Guid ann = _directory.Add(1, "Ann");
        Guid bob = _directory.Add(2, "Bob");
        _presence.McpSeen(ann);
        _presence.McpSeen(bob);

        var sent = Assert.IsType<MemberSendResult.Sent>(await SendAsync(Request(audience: "allOnline")));

        Assert.Equal(2, sent.Result.Recipients);
        Assert.Equal(new[] { ann, bob }.Order(), Assert.Single(_banner.Sent).MemberKeys.Order());
    }

    [Theory]
    [InlineData("everyone")]
    [InlineData(null)]
    public async Task An_unknown_audience_is_rejected(string? audience)
    {
        Assert.IsType<MemberSendResult.Rejected>(await SendAsync(Request(audience: audience)));
        Assert.Empty(_banner.Sent);
    }

    [Fact]
    public async Task Selected_needs_someone_selected()
    {
        Assert.IsType<MemberSendResult.Rejected>(await SendAsync(Request(audience: "selected", memberKeys: [])));
    }

    [Fact]
    public async Task A_member_selected_twice_is_sent_to_once()
    {
        Guid ann = Guid.NewGuid();

        var sent = Assert.IsType<MemberSendResult.Sent>(
            await SendAsync(Request(audience: "selected", memberKeys: [ann, ann])));

        Assert.Equal(1, sent.Result.Recipients);
    }

    [Fact]
    public async Task Only_delivery_methods_the_site_has_count()
    {
        Assert.IsType<MemberSendResult.Rejected>(await SendAsync(Request(senders: ["email"])));
        Assert.IsType<MemberSendResult.Sent>(await SendAsync(Request(senders: ["BANNER"])));
    }

    [Fact]
    public async Task A_message_must_stop_being_shown_in_the_future()
    {
        Assert.IsType<MemberSendResult.Rejected>(await SendAsync(Request() with { ExpiresAt = _clock.GetUtcNow() }));
        Assert.IsType<MemberSendResult.Rejected>(await SendAsync(Request() with { ExpiresAt = null }));
        Assert.Empty(_banner.Sent);
    }

    [Fact]
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

        Assert.Equal("Not during the release freeze.", Assert.IsType<MemberSendResult.Rejected>(result).Reason);
        Assert.Empty(_banner.Sent);
        Assert.Empty(_events.Published.OfType<MemberMessageSentNotification>());
    }

    [Fact]
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

        Assert.Equal(CancelledSend.DefaultReason, Assert.IsType<MemberSendResult.Rejected>(result).Reason);
    }

    [Fact]
    public async Task Sending_comes_before_delivery_with_the_message_its_delivery_methods_and_its_sender()
    {
        Guid ann = _directory.Add(1, "Ann");
        _presence.McpSeen(ann);
        _events.OnPublish = n =>
        {
            if (n is MemberMessageSendingNotification)
            {
                Assert.Empty(_banner.Sent);
            }
        };

        await SendAsync(Request(audience: "allOnline"));

        var sending = _events.Single<MemberMessageSendingNotification>();
        Assert.Equal(["banner"], sending.DeliveryMethods);
        Assert.Equal(_meKey, sending.SenderKey);
        Assert.Equal([ann], sending.Message.MemberKeys);
    }

    [Fact]
    public async Task Sent_comes_after_delivery_with_what_each_delivery_method_did()
    {
        var sent = Assert.IsType<MemberSendResult.Sent>(await SendAsync(Request()));

        var notification = Assert.IsType<MemberMessageSentNotification>(_events.Published.Last());
        Assert.Equal(sent.Result.Key, notification.Message.Key);
        Assert.Equal(_meKey, notification.SenderKey);
        MemberMessageSenderResult result = Assert.Single(notification.Results);
        Assert.Equal("banner", result.Alias);
        Assert.Equal(sent.Result.Results[0].Delivered, result.Delivered);
    }

    [Fact]
    public async Task An_invalid_message_publishes_nothing()
    {
        await SendAsync(Request(headline: "   "));

        Assert.Empty(_events.Published);
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
