using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Umbricians.LiveView.Configuration;

/// <summary>
/// Adds the channels declared in code and the built-in channels, and fills any property appsettings left empty on
/// them. Appsettings values win, then channels declared in code, then the built-in defaults, whatever order
/// composers run in.
/// </summary>
internal sealed class LiveViewOptionsDefaults(IEnumerable<LiveViewChannelRegistration> registrations)
    : IPostConfigureOptions<LiveViewOptions>
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

    /// <summary>Registers this post-configure, once however often it is called.</summary>
    internal static void AddTo(IServiceCollection services) =>
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<LiveViewOptions>, LiveViewOptionsDefaults>());

    public void PostConfigure(string? name, LiveViewOptions options)
    {
        foreach (LiveViewChannelRegistration registration in registrations)
        {
            Fill(options, registration.Alias, registration.Defaults);
        }

        foreach ((string alias, LiveViewChannelOptions defaults) in BuiltInChannels)
        {
            Fill(options, alias, defaults);
        }
    }

    /// <summary>Adds the channel if it is missing, then copies in each value it still has empty.</summary>
    private static void Fill(LiveViewOptions options, string alias, LiveViewChannelOptions defaults)
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
