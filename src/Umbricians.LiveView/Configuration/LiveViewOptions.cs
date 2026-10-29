namespace Umbricians.LiveView.Configuration;

/// <summary>
/// Options bound from the <c>Umbricians:LiveView</c> configuration section.
/// </summary>
public class LiveViewOptions
{
    /// <summary>Turns request tracking on or off. The dashboard still shows custom metric providers when off.</summary>
    public bool TrackingEnabled { get; set; } = true;

    /// <summary>How long a session counts as active after its last request, unless a channel overrides it.</summary>
    public int ActivityWindowSeconds { get; set; } = 300;

    /// <summary>Requests whose User-Agent looks like a crawler are not tracked.</summary>
    public bool IgnoreBots { get; set; } = true;

    /// <summary>Upper bound on sessions held in memory per channel, to protect the server from floods.</summary>
    public int MaxSessionsPerChannel { get; set; } = 100_000;

    /// <summary>File extensions ignored by channels with <see cref="LiveViewChannelOptions.IgnoreStaticFiles"/> set.</summary>
    public string[] StaticFileExtensions { get; set; } =
    [
        ".css", ".js", ".mjs", ".map", ".json", ".xml", ".txt", ".ico", ".png", ".jpg", ".jpeg", ".gif",
        ".svg", ".webp", ".avif", ".woff", ".woff2", ".ttf", ".eot", ".mp4", ".webm", ".pdf",
    ];

    /// <summary>
    /// Request-tracked channels, keyed by alias. The built-in <c>web</c>, <c>members</c>, <c>backoffice</c>, <c>mcp</c> and <c>api</c> channels are
    /// added when not configured; any property left empty on them falls back to its built-in value.
    /// </summary>
    public Dictionary<string, LiveViewChannelOptions> Channels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class LiveViewChannelOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Label shown on the dashboard.</summary>
    public string? Name { get; set; }

    /// <summary>Line colour on the dashboard, any CSS colour.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// Requests are matched against channels in ascending order and counted by each match, stopping at the first
    /// <see cref="Exclusive"/> one.
    /// </summary>
    public int? SortOrder { get; set; }

    /// <summary>
    /// Path prefixes this channel tracks, matched on whole segments (<c>/api</c> matches <c>/api/x</c>, not <c>/apix</c>).
    /// A channel with no prefixes is only fed from code through <see cref="Tracking.ILiveActivityTracker"/>.
    /// </summary>
    public string[]? PathPrefixes { get; set; }

    /// <summary>Path prefixes excluded from this channel.</summary>
    public string[]? ExcludePathPrefixes { get; set; }

    /// <summary>
    /// Where the session identity comes from, tried in order: <c>Header:Name</c>, <c>Cookie:Name</c>,
    /// <c>Authorization</c>, <c>User</c> (signed-in user id) or <c>Client</c> (IP + User-Agent). Values are hashed before they are held in memory.
    /// </summary>
    public string[]? SessionKeys { get; set; }

    public int? ActivityWindowSeconds { get; set; }

    public bool? IgnoreStaticFiles { get; set; }

    /// <summary>
    /// When true (the default), a request this channel counts is not offered to later channels. Set it to false
    /// for a channel that overlaps another, such as logged-in members, who are also web visitors.
    /// </summary>
    public bool? Exclusive { get; set; }
}
