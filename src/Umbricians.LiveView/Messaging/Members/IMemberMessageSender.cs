namespace Umbricians.LiveView.Messaging.Members;

/// <summary>
/// Delivers messages written on the Live View dashboard to website members. The package collects the message and
/// works out who it is for; each site decides how it reaches them (SignalR, a banner on their next page view, email,
/// push notifications and so on).
/// </summary>
/// <remarks>
/// Register with <c>builder.AddLiveViewMemberMessageSender&lt;T&gt;()</c>. The dashboard only offers member messaging once at
/// least one sender is registered, and lets the sender of a message choose which senders to use.
/// </remarks>
public interface IMemberMessageSender
{
    /// <summary>Unique, stable key for the sender.</summary>
    string Alias { get; }

    /// <summary>Shown as a delivery option on the dashboard, e.g. "Live (SignalR)" or "Banner on next page".</summary>
    string Name { get; }

    /// <summary>A short explanation shown under the delivery option.</summary>
    string? Description => null;

    /// <summary>
    /// Whether this sender can reach members who are not logged in right now, for example by email or with a banner
    /// on their next visit. Shown as a hint when a message is addressed to every member.
    /// </summary>
    bool ReachesOfflineMembers => false;

    /// <summary>
    /// Delivers the message. The body is plain text: encode it for wherever it is displayed.
    /// Senders are given 30 seconds; a failure or timeout is reported on the dashboard and does not stop other senders.
    /// </summary>
    Task<MemberMessageResult> SendAsync(MemberMessage message, CancellationToken cancellationToken);
}

/// <summary>How the dashboard presents the message, which senders can map to their own styling.</summary>
public enum MemberMessageType
{
    /// <summary>General news, such as a new feature.</summary>
    Information,

    /// <summary>Something members should know about, such as planned maintenance.</summary>
    Warning,

    /// <summary>Something members need to act on now.</summary>
    Urgent,

    /// <summary>Good news, such as a problem fixed.</summary>
    GoodNews,
}

/// <summary>Who a member message is for, as chosen on the dashboard.</summary>
public enum MemberMessageAudience
{
    /// <summary>Members picked on the dashboard. <see cref="MemberMessage.MemberKeys"/> lists them.</summary>
    Selected,

    /// <summary>Everyone logged in when the message was sent. <see cref="MemberMessage.MemberKeys"/> lists them.</summary>
    AllOnline,

    /// <summary>
    /// Every member of the site, including those not logged in. <see cref="MemberMessage.MemberKeys"/> is empty, as
    /// the audience may be very large: reach them in whatever way suits the sender.
    /// </summary>
    AllMembers,
}

/// <param name="Key">Unique id for the message, the same for every sender.</param>
/// <param name="Headline">The headline, up to 200 characters of plain text.</param>
/// <param name="Body">Plain text; may be empty.</param>
/// <param name="Type">How the dashboard presents the message, to map to the sender's own styling.</param>
/// <param name="Audience">Who the message is for.</param>
/// <param name="MemberKeys">The keys of the members it is for, or empty for <see cref="MemberMessageAudience.AllMembers"/>.</param>
/// <param name="SenderName">The name of the backoffice user who sent it, or null when it has no sender.</param>
/// <param name="SentAt">When it was sent.</param>
/// <param name="ExpiresAt">When the message stops being relevant, e.g. once a restart has happened.</param>
public sealed record MemberMessage(
    Guid Key,
    string Headline,
    string Body,
    MemberMessageType Type,
    MemberMessageAudience Audience,
    IReadOnlyList<Guid> MemberKeys,
    string? SenderName,
    DateTimeOffset SentAt,
    DateTimeOffset ExpiresAt);

/// <param name="Delivered">How many members the message was sent or queued for, if the sender knows.</param>
/// <param name="Note">Shown next to the result on the dashboard, e.g. "queued for their next page view".</param>
public sealed record MemberMessageResult(int? Delivered, string? Note = null);

/// <summary>What one delivery method did with a member message.</summary>
/// <param name="Alias">The <see cref="IMemberMessageSender.Alias"/> of the sender.</param>
/// <param name="Name">The <see cref="IMemberMessageSender.Name"/> of the sender.</param>
/// <param name="Delivered">The <see cref="MemberMessageResult.Delivered"/> the sender returned, or null if it did not deliver.</param>
/// <param name="Note">The <see cref="MemberMessageResult.Note"/> the sender returned.</param>
/// <param name="Error"><c>Failed</c> or <c>Timed out</c> when the sender did not deliver; otherwise null.</param>
public sealed record MemberMessageSenderResult(string Alias, string Name, int? Delivered, string? Note, string? Error);
