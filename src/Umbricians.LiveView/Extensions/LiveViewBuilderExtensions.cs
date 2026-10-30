using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Metrics;

namespace Umbricians.LiveView.Extensions;

public static class LiveViewBuilderExtensions
{
    /// <summary>Adds a custom line to the Live View dashboard.</summary>
    public static IUmbracoBuilder AddLiveViewMetric<TProvider>(this IUmbracoBuilder builder)
        where TProvider : class, ILiveMetricProvider
    {
        builder.Services.AddSingleton<ILiveMetricProvider, TProvider>();
        return builder;
    }

    /// <summary>
    /// Adds a way of delivering dashboard messages to website members, such as SignalR, a banner or email. Member
    /// messaging appears on the dashboard once at least one sender is registered.
    /// </summary>
    public static IUmbracoBuilder AddMemberMessageSender<TSender>(this IUmbracoBuilder builder)
        where TSender : class, IMemberMessageSender
    {
        builder.Services.AddSingleton<IMemberMessageSender, TSender>();
        return builder;
    }

    /// <summary>
    /// Declares a tracked channel. Give it <see cref="LiveViewChannelOptions.PathPrefixes"/> to have requests
    /// counted automatically, or leave them empty and feed it from code with
    /// <see cref="Tracking.ILiveActivityTracker"/>. Values set in appsettings still take precedence.
    /// </summary>
    public static IUmbracoBuilder AddLiveViewChannel(
        this IUmbracoBuilder builder,
        string alias,
        string name,
        Action<LiveViewChannelOptions>? configure = null)
    {
        builder.Services.PostConfigure<LiveViewOptions>(options =>
        {
            if (!options.Channels.TryGetValue(alias, out LiveViewChannelOptions? channel))
            {
                options.Channels[alias] = channel = new LiveViewChannelOptions();
            }

            var defaults = new LiveViewChannelOptions { Name = name };
            configure?.Invoke(defaults);

            channel.Name ??= defaults.Name;
            channel.Color ??= defaults.Color;
            channel.SortOrder ??= defaults.SortOrder;
            channel.PathPrefixes ??= defaults.PathPrefixes;
            channel.ExcludePathPrefixes ??= defaults.ExcludePathPrefixes;
            channel.SessionKeys ??= defaults.SessionKeys;
            channel.ActivityWindowSeconds ??= defaults.ActivityWindowSeconds;
            channel.IgnoreStaticFiles ??= defaults.IgnoreStaticFiles;
            channel.Exclusive ??= defaults.Exclusive;
        });

        return builder;
    }
}
