namespace Umbricians.LiveView.Messaging;

/// <summary>A message sent to backoffice users, as their backoffice fetches it.</summary>
/// <param name="Key">Identifies the message; it is what a server event carries.</param>
/// <param name="Headline">The headline, up to 200 characters.</param>
/// <param name="Message">The message under the headline, up to 2000 characters of plain text; may be empty.</param>
/// <param name="Color">The message's type as an Umbraco UI colour: <c>default</c>, <c>positive</c>, <c>warning</c> or <c>danger</c>.</param>
/// <param name="SenderName">The name of the user who sent it, or null when it has no sender.</param>
/// <param name="SentAt">When it was sent.</param>
public sealed record BackofficeMessage(
    Guid Key,
    string Headline,
    string Message,
    string Color,
    string? SenderName,
    DateTimeOffset SentAt);

/// <summary>
/// Where sent backoffice messages wait for their recipients' backoffice to fetch them. In memory by default; replace
/// it so a recipient whose heartbeat reaches another server still gets the message.
/// </summary>
/// <remarks>The store only holds messages: who a message is sent to and how long it lives are decided by Live View.</remarks>
public interface IBackofficeMessageStore
{
    /// <summary>Keeps the message for these recipients, none delivered yet, until <paramref name="expiresAt"/> (it may be discarded after).</summary>
    Task AddAsync(BackofficeMessage message, IReadOnlyCollection<Guid> recipients, DateTimeOffset expiresAt);

    /// <summary>The message if <paramref name="userKey"/> is one of its recipients, marked delivered to them; otherwise null.</summary>
    Task<BackofficeMessage?> ReceiveAsync(Guid messageKey, Guid userKey);

    /// <summary>Keys of the unexpired messages for the user that have not been delivered to them yet.</summary>
    Task<IReadOnlyList<Guid>> GetUndeliveredAsync(Guid userKey);
}
