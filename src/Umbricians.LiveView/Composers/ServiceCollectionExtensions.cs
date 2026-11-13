using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Controllers;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Metrics;
using Umbricians.LiveView.OpenApi;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Composers;

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything Live View needs except its notification handlers, which need the Umbraco builder.
    /// </summary>
    public static IServiceCollection AddLiveView(this IServiceCollection services, IConfiguration configuration)
    {
        // Binds the Live View section, then fills defaults and replaces unusable values.
        services.AddOptions<LiveViewOptions>().Bind(configuration.GetSection(Constants.ConfigurationSection));
        LiveViewOptionsDefaults.AddTo(services);
        LiveViewOptionsValidation.AddTo(services);

        services.AddMvcCore().ConfigureApplicationPartManager(parts =>
            parts.FeatureProviders.Add(new LiveViewControllerFeatureProvider()));
        services.ConfigureOptions<LiveViewSwaggerGenOptions>();

        services.TryAddSingleton(TimeProvider.System);

        // A site's own stores, avatar provider and inbox win whichever composer runs first: TryAdd skips ours if one
        // is already there, and a registration after ours is the one resolved. The tracker takes the store, so it is
        // registered the same way.
        services.TryAddSingleton<ILiveViewActivityStore, InMemoryActivityStore>();
        services.TryAddSingleton<IBackofficeMessageStore, InMemoryBackofficeMessageStore>();
        services.TryAddSingleton<ILiveActivityTracker, LiveActivityTracker>();
        services.TryAddSingleton<IMemberAvatarProvider, PropertyMemberAvatarProvider>();
        services.TryAddSingleton<IMemberMessageInbox, MemberMessageInbox>();

        services.AddSingleton<RequestClassifier>();
        services.AddSingleton<UnforwardedProxyDetector>();
        services.AddSingleton<LiveActivityMiddleware>();
        services.AddSingleton<LiveMetricsService>();
        services.AddSingleton<IBackofficeUserDirectory, UmbracoBackofficeUserDirectory>();
        services.AddSingleton(sp => new BackofficePresence(
            sp.GetRequiredService<ILiveViewActivityStore>(),
            sp.GetRequiredService<IBackofficeUserDirectory>(),
            sp.GetRequiredService<IOptionsMonitor<LiveViewOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IMemberDirectory, UmbracoMemberDirectory>();
        services.AddSingleton(sp => new MemberPresence(
            sp.GetRequiredService<ILiveViewActivityStore>(),
            sp.GetRequiredService<IMemberDirectory>(),
            sp.GetRequiredService<IOptionsMonitor<LiveViewOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new McpCallers(
            sp.GetRequiredService<BackofficePresence>(),
            sp.GetRequiredService<MemberPresence>(),
            sp.GetRequiredService<IBackofficeUserDirectory>(),
            sp.GetRequiredService<IMemberDirectory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<McpCallers>>()));
        services.AddSingleton<ILiveViewPresence>(sp => sp.GetRequiredService<McpCallers>());
        services.AddSingleton<BackofficeMessenger>();
        services.AddSingleton<MemberMessagingService>();

        services.Configure<UmbracoPipelineOptions>(options =>
            options.AddFilter(new UmbracoPipelineFilter("Umbricians.LiveView")
            {
                PrePipeline = app => app.UseMiddleware<LiveActivityMiddleware>(),
            }));

        return services;
    }
}
