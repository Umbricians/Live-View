using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Messaging;

/// <summary>A message as the dashboard sends it: text untrimmed, colour unchecked, recipients not yet worked out.</summary>
internal sealed record BackofficeMessageRequest(
    string? Headline,
    string? Message,
    string? Color,
    IReadOnlyCollection<Guid>? UserKeys,
    IReadOnlyCollection<Guid>? UserGroupKeys,
    bool AllOnline);

/// <summary>
/// The backoffice user sending a message, who is never one of its recipients. Only a sender with access to the
/// Users section can choose user groups, the same rule the dialog's group picker has.
/// </summary>
internal sealed record BackofficeSender(Guid? Key, string? Name, bool CanChooseUserGroups = false);

internal abstract record BackofficeSendResult
{
    private BackofficeSendResult()
    {
    }

    internal sealed record Sent(BackofficeMessage Message, int Recipients) : BackofficeSendResult;

    internal sealed record Rejected(string Reason) : BackofficeSendResult;
}

/// <summary>
/// Sends a one-off notification to chosen backoffice users, e.g. to warn them before an upgrade or restart.
/// </summary>
/// <remarks>
/// It owns the send rules: the message content (see <see cref="MessageContent"/>), who "all online" and the chosen
/// user groups mean at the moment of sending, that the sender is never a recipient, and that only a sender with
/// access to the Users section can choose user groups. A user group audience is everyone online in the backoffice
/// in any of the groups, which must all exist. Selected users are sent to as chosen, even if they have since gone
/// offline, and pick the message up if they come back within its lifetime.
/// Sites can stop or follow a send with <see cref="BackofficeMessageSendingNotification"/> and <see cref="BackofficeMessageSentNotification"/>.
/// Server events only carry a key, so the message is held in the <see cref="IBackofficeMessageStore"/> and each
/// recipient's backoffice fetches it by key. Recipients whose realtime connection is down pick it up from their next
/// heartbeat instead. On a load-balanced site only the recipients connected to this server get the instant push; the
/// others pick the message up from their next heartbeat. Messages are discarded after <see cref="LiveViewOptions.BackofficeMessageLifetimeMinutes"/>.
/// </remarks>
internal sealed class BackofficeMessenger
{
    public const string EventSource = "Umbricians.LiveView";
    public const string EventType = "Message";

    internal const int MaxRecipients = 1000;
    internal const int MaxUserGroups = 100;
    internal const string UserGroupsNeedUsersSection = "Choosing user groups needs access to the Users section.";

    private readonly IServerEventRouter _router;
    private readonly BackofficePresence _presence;
    private readonly IBackofficeUserDirectory _directory;
    private readonly IBackofficeMessageStore _store;
    private readonly IEventAggregator _eventAggregator;
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    public BackofficeMessenger(
        IServerEventRouter router,
        BackofficePresence presence,
        IBackofficeUserDirectory directory,
        IBackofficeMessageStore store,
        IEventAggregator eventAggregator,
        IOptionsMonitor<LiveViewOptions> options,
        TimeProvider timeProvider)
    {
        _router = router;
        _presence = presence;
        _directory = directory;
        _store = store;
        _eventAggregator = eventAggregator;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// How many users a message to everyone online would reach right now: everyone in the backoffice but the sender.
    /// Users only on MCP are left out, since the message is shown in a backoffice tab.
    /// </summary>
    internal async Task<int> CountAllOnlineRecipientsAsync(Guid? senderKey) =>
        (await _presence.AllInBackofficeAsync()).Count(k => k != senderKey);

    /// <summary>
    /// How many users a message to these user groups would reach right now: everyone in the backoffice in any of them
    /// but the sender. Or why the groups can't be sent to.
    /// </summary>
    internal async Task<(int Recipients, string? Rejected)> CountUserGroupRecipientsAsync(
        IReadOnlyCollection<Guid> userGroupKeys, BackofficeSender sender)
    {
        if (!sender.CanChooseUserGroups)
        {
            return (0, UserGroupsNeedUsersSection);
        }

        (IReadOnlyList<Guid> users, string? rejected) = await InUserGroupsAsync(userGroupKeys);
        return (users.Count(k => k != sender.Key), rejected);
    }

    /// <summary>Checks the message, works out its recipients and delivers it, or says why it can't be sent.</summary>
    internal async Task<BackofficeSendResult> SendAsync(BackofficeMessageRequest request, BackofficeSender sender)
    {
        if (!MessageContent.TryCreate(request.Headline, request.Message, request.Color, out MessageContent content))
        {
            return new BackofficeSendResult.Rejected(MessageContent.Limits);
        }

        // "All online" and user groups are resolved here rather than in the browser, which only ever holds one page of users.
        IReadOnlyCollection<Guid> chosen;
        if (request.AllOnline)
        {
            chosen = await _presence.AllInBackofficeAsync();
        }
        else if (request.UserGroupKeys is { } userGroupKeys)
        {
            if (!sender.CanChooseUserGroups)
            {
                return new BackofficeSendResult.Rejected(UserGroupsNeedUsersSection);
            }

            (IReadOnlyList<Guid> inGroups, string? rejected) = await InUserGroupsAsync(userGroupKeys);
            if (rejected is not null)
            {
                return new BackofficeSendResult.Rejected(rejected);
            }

            chosen = inGroups;
        }
        else
        {
            chosen = request.UserKeys ?? [];
        }

        Guid[] recipients = chosen
            .Where(k => k != sender.Key)
            .Distinct()
            .ToArray();

        if (recipients.Length is 0 or > MaxRecipients)
        {
            return new BackofficeSendResult.Rejected($"Choose between 1 and {MaxRecipients} other users to send to.");
        }

        var sent = new BackofficeMessage(
            Guid.NewGuid(), content.Headline, content.Message, content.Color, sender.Name, _timeProvider.GetUtcNow());

        var sending = new BackofficeMessageSendingNotification(sent, recipients, sender.Key, new EventMessages());
        if (await _eventAggregator.PublishCancelableAsync(sending))
        {
            return new BackofficeSendResult.Rejected(CancelledSend.Reason(sending.Messages));
        }

        await _store.AddAsync(sent, recipients, sent.SentAt + TimeSpan.FromMinutes(_options.CurrentValue.BackofficeMessageLifetimeMinutes));

        var serverEvent = new ServerEvent { EventSource = EventSource, EventType = EventType, Key = sent.Key };
        await Task.WhenAll(recipients.Select(r => _router.NotifyUserAsync(serverEvent, r)));

        await _eventAggregator.PublishAsync(new BackofficeMessageSentNotification(sent, recipients, sender.Key));

        return new BackofficeSendResult.Sent(sent, recipients.Length);
    }

    /// <summary>Returns the message if the user is a recipient, and marks it delivered to them.</summary>
    public Task<BackofficeMessage?> ReceiveAsync(Guid messageKey, Guid userKey) => _store.ReceiveAsync(messageKey, userKey);

    /// <summary>
    /// Everyone online in the backoffice who is in any of the user groups, read once, or why the groups can't be sent
    /// to. As for everyone online, users only on MCP are left out, and so are users whose account has gone or been
    /// disabled.
    /// </summary>
    private async Task<(IReadOnlyList<Guid> Users, string? Rejected)> InUserGroupsAsync(
        IReadOnlyCollection<Guid> userGroupKeys)
    {
        Guid[] groups = userGroupKeys.Distinct().ToArray();
        if (groups.Length is 0 or > MaxUserGroups)
        {
            return ([], $"Choose between 1 and {MaxUserGroups} user groups to send to.");
        }

        if ((await _directory.FindUserGroupsAsync(groups)).Count != groups.Length)
        {
            return ([], "A user group you chose no longer exists.");
        }

        IReadOnlyList<BackofficeUserDetails> online = await _directory.GetAsync(await _presence.AllInBackofficeAsync());
        return (online.Where(u => u.UserGroupKeys.Any(groups.Contains)).Select(u => u.Key).ToList(), null);
    }

    /// <summary>Keys of messages sent to the user that their backoffice has not fetched yet.</summary>
    public Task<IReadOnlyList<Guid>> GetUndeliveredAsync(Guid userKey) => _store.GetUndeliveredAsync(userKey);
}
