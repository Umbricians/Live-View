namespace Umbricians.LiveView.Messaging.Members;

/// <summary>
/// The rules for when a member message stops being relevant, chosen on the dashboard as a time of day or a duration.
/// </summary>
internal static class MemberMessageExpiry
{
    public static readonly TimeSpan Longest = TimeSpan.FromDays(30);

    /// <summary>
    /// Accepts an expiry in the future, moved back to <see cref="Longest"/> from now when it is later, so a browser
    /// clock a little ahead of the server's doesn't turn the longest choice away.
    /// </summary>
    public static bool TryResolve(DateTimeOffset? requested, DateTimeOffset now, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (requested is not { } value || value <= now)
        {
            return false;
        }

        expiresAt = value > now + Longest ? now + Longest : value;
        return true;
    }

    /// <summary>
    /// The IANA id of the configured time zone, which browsers understand, given an IANA or Windows id; null when it
    /// is not set or not known on this server.
    /// </summary>
    public static string? TimeZoneId(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(configured.Trim(), out TimeZoneInfo? zone))
        {
            return null;
        }

        if (zone.HasIanaId)
        {
            return zone.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out string? iana) ? iana : null;
    }
}
