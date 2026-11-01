namespace Umbricians.LiveView.Configuration;

/// <summary>
/// A channel's settings as they apply, with every value filled in. Which of appsettings, code and the built-in
/// defaults a value comes from is settled when the options are configured; this fills what is still empty, e.g. on a
/// channel declared only in appsettings, and is the one place anything reads a channel's settings from.
/// </summary>
internal sealed record ChannelSettings(
    string Alias,
    string Name,
    string? Color,
    int SortOrder,
    bool Enabled,
    string[] PathPrefixes,
    string[] ExcludePathPrefixes,
    string[] SessionKeys,
    TimeSpan ActivityWindow,
    bool IgnoreStaticFiles,
    bool Exclusive)
{
    /// <summary>The channel's settings; a channel with no settings at all gets the defaults.</summary>
    public static ChannelSettings Of(LiveViewOptions options, string alias) =>
        From(options, alias, options.Channels.TryGetValue(alias, out LiveViewChannelOptions? channel) ? channel : new());

    /// <summary>Every channel's settings, in sort order.</summary>
    public static IEnumerable<ChannelSettings> InOrder(LiveViewOptions options) =>
        options.Channels.Select(c => From(options, c.Key, c.Value)).OrderBy(c => c.SortOrder);

    private static ChannelSettings From(LiveViewOptions options, string alias, LiveViewChannelOptions channel) =>
        new(
            alias,
            channel.Name ?? alias,
            channel.Color,
            channel.SortOrder ?? 0,
            channel.Enabled,
            channel.PathPrefixes ?? [],
            channel.ExcludePathPrefixes ?? [],
            channel.SessionKeys ?? [Constants.SessionKeySources.Client],
            TimeSpan.FromSeconds(channel.ActivityWindowSeconds ?? options.ActivityWindowSeconds),
            channel.IgnoreStaticFiles ?? false,
            channel.Exclusive ?? true);
}
