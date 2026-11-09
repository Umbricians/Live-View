using Umbricians.LiveView.Metrics;

namespace Umbricians.LiveView.Configuration;

/// <summary>
/// A channel's settings as they apply, with every value filled in. Which of appsettings, code and the built-in
/// defaults a value comes from is settled when the options are configured; this fills what is still empty, e.g. on a
/// channel declared only in appsettings, and is the one place anything reads a channel's settings from. A metric provider's line reads its settings here too.
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

    /// <summary>
    /// A metric provider's line. Only whether it is shown, its name, colour and sort order apply to it: the settings under
    /// the provider's alias win, then the provider's own values, then the defaults.
    /// </summary>
    public static ChannelSettings Of(LiveViewOptions options, ILiveMetricProvider provider)
    {
        LiveViewChannelOptions configured =
            options.Channels.TryGetValue(provider.Alias, out LiveViewChannelOptions? channel) ? channel : new();
        LiveViewChannelOptions line = new()
        {
            Enabled = configured.Enabled,
            Name = configured.Name,
            Color = configured.Color,
            SortOrder = configured.SortOrder,
        };
        LiveViewOptionsDefaults.FillGaps(
            line, new LiveViewChannelOptions { Name = provider.Name, Color = provider.Color, SortOrder = provider.SortOrder });
        return From(options, provider.Alias, line);
    }

    /// <summary>
    /// Every channel's settings, in sort order. Settings under a metric provider's alias are for its line, not a
    /// channel, so they are left out.
    /// </summary>
    public static IEnumerable<ChannelSettings> InOrder(LiveViewOptions options, IEnumerable<ILiveMetricProvider> providers) =>
        options.Channels
            .Where(c => !providers.Any(p => string.Equals(p.Alias, c.Key, StringComparison.OrdinalIgnoreCase)))
            .Select(c => From(options, c.Key, c.Value))
            .OrderBy(c => c.SortOrder);

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
