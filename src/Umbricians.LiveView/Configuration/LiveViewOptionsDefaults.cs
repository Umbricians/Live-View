using Microsoft.Extensions.Options;

namespace Umbricians.LiveView.Configuration;

/// <summary>
/// Adds the built-in channels and fills any property a site left empty on them.
/// </summary>
internal sealed class LiveViewOptionsDefaults : IPostConfigureOptions<LiveViewOptions>
{
    private static readonly string[] WebExcludePathPrefixes =
    [
        "/umbraco", "/App_Plugins", "/media", "/install", "/.well-known", "/_framework", "/sb", "/mcp", "/api",
    ];

    internal static IReadOnlyDictionary<string, LiveViewChannelOptions> BuiltInChannels { get; } =
        new Dictionary<string, LiveViewChannelOptions>(StringComparer.OrdinalIgnoreCase)
        {
            [Constants.Channels.Members] = new()
            {
                Name = "Members online",
                Color = "#0ea5e9",
                SortOrder = 90,
                PathPrefixes = ["/"],
                ExcludePathPrefixes = WebExcludePathPrefixes,
                SessionKeys = [Constants.SessionKeySources.User],
                IgnoreStaticFiles = true,

                // A logged-in member is also a web visitor, so keep matching and count them in Web sessions too.
                Exclusive = false,
            },
            [Constants.Channels.Web] = new()
            {
                Name = "Web sessions",
                Color = "#3544b1",
                SortOrder = 100,
                PathPrefixes = ["/"],
                ExcludePathPrefixes = WebExcludePathPrefixes,
                SessionKeys = [Constants.SessionKeySources.Client],
                IgnoreStaticFiles = true,
            },
            [Constants.Channels.Mcp] = new()
            {
                Name = "MCP sessions",
                Color = "#f79c37",
                SortOrder = 10,
                PathPrefixes = ["/mcp", "/umbraco/mcp"],
                SessionKeys =
                [
                    Constants.SessionKeySources.HeaderPrefix + "Mcp-Session-Id",
                    Constants.SessionKeySources.Authorization,
                    Constants.SessionKeySources.Client,
                ],
                IgnoreStaticFiles = false,
            },
            [Constants.Channels.Backoffice] = new()
            {
                Name = "Backoffice users",
                Color = "#d42054",
                SortOrder = 50,
                PathPrefixes = ["/umbraco"],
                SessionKeys = [Constants.SessionKeySources.User],
                IgnoreStaticFiles = true,
            },
            [Constants.Channels.Api] = new()
            {
                Name = "API sessions",
                Color = "#2bc37c",
                SortOrder = 20,
                PathPrefixes = ["/api", "/umbraco/delivery/api"],
                SessionKeys =
                [
                    Constants.SessionKeySources.HeaderPrefix + "Api-Key",
                    Constants.SessionKeySources.Authorization,
                    Constants.SessionKeySources.Client,
                ],
                ActivityWindowSeconds = 60,
                IgnoreStaticFiles = false,
            },
        };

    public void PostConfigure(string? name, LiveViewOptions options)
    {
        foreach ((string alias, LiveViewChannelOptions defaults) in BuiltInChannels)
        {
            if (!options.Channels.TryGetValue(alias, out LiveViewChannelOptions? channel))
            {
                options.Channels[alias] = channel = new LiveViewChannelOptions();
            }

            channel.Name ??= defaults.Name;
            channel.Color ??= defaults.Color;
            channel.SortOrder ??= defaults.SortOrder;
            channel.PathPrefixes ??= defaults.PathPrefixes;
            channel.ExcludePathPrefixes ??= defaults.ExcludePathPrefixes;
            channel.SessionKeys ??= defaults.SessionKeys;
            channel.ActivityWindowSeconds ??= defaults.ActivityWindowSeconds;
            channel.IgnoreStaticFiles ??= defaults.IgnoreStaticFiles;
            channel.Exclusive ??= defaults.Exclusive;
        }
    }
}
