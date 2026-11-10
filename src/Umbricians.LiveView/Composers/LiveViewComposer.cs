using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Controllers;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Metrics;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Composers;

public class LiveViewComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<LiveViewOptions>().Bind(builder.Config.GetSection(Constants.ConfigurationSection));
        LiveViewOptionsDefaults.AddTo(builder.Services);

        builder.Services.AddMvcCore().ConfigureApplicationPartManager(parts =>
            parts.FeatureProviders.Add(new LiveViewControllerFeatureProvider()));
        builder.Services.TryAddSingleton(TimeProvider.System);
        AddStores(builder.Services);
        builder.Services.AddSingleton<RequestClassifier>();
        builder.Services.AddSingleton<LiveActivityMiddleware>();
        builder.Services.AddSingleton<LiveMetricsService>();
        builder.Services.AddSingleton<IBackofficeUserDirectory, UmbracoBackofficeUserDirectory>();
        builder.Services.AddSingleton(sp => new BackofficePresence(
            sp.GetRequiredService<ILiveViewActivityStore>(),
            sp.GetRequiredService<IBackofficeUserDirectory>(),
            sp.GetRequiredService<IOptionsMonitor<LiveViewOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.AddNotificationAsyncHandler<UserDeletedNotification, BackofficeUserDeletedHandler>();
        builder.AddNotificationAsyncHandler<UserSavedNotification, BackofficeUserDisabledHandler>();
        // A site's own avatar provider wins whichever composer runs first: TryAdd skips ours if it is already
        // there, and a registration after ours is the one resolved.
        builder.Services.TryAddSingleton<IMemberAvatarProvider, PropertyMemberAvatarProvider>();
        builder.Services.AddSingleton<IMemberDirectory, UmbracoMemberDirectory>();
        builder.Services.AddSingleton(sp => new MemberPresence(
            sp.GetRequiredService<ILiveViewActivityStore>(),
            sp.GetRequiredService<IMemberDirectory>(),
            sp.GetRequiredService<IOptionsMonitor<LiveViewOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(sp => new McpCallers(
            sp.GetRequiredService<BackofficePresence>(),
            sp.GetRequiredService<MemberPresence>(),
            sp.GetRequiredService<IBackofficeUserDirectory>(),
            sp.GetRequiredService<IMemberDirectory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<McpCallers>>()));
        builder.Services.AddSingleton<ILiveViewPresence>(sp => sp.GetRequiredService<McpCallers>());
        builder.AddNotificationAsyncHandler<MemberLogoutSuccessNotification, MemberLogoutHandler>();
        builder.AddNotificationAsyncHandler<MemberDeletedNotification, MemberDeletedHandler>();
        builder.Services.AddSingleton<BackofficeMessenger>();
        builder.Services.AddSingleton<MemberMessagingService>();
        builder.Services.TryAddSingleton<IMemberMessageInbox, MemberMessageInbox>();

        builder.Services.Configure<UmbracoPipelineOptions>(options =>
            options.AddFilter(new UmbracoPipelineFilter("Umbricians.LiveView")
            {
                PrePipeline = app => app.UseMiddleware<LiveActivityMiddleware>(),
            }));
    }

    // A site's own stores win whichever composer runs first: TryAdd skips ours if one is already there, and a
    // registration after ours is the one resolved. The tracker takes the store, so it is registered the same way.
    internal static void AddStores(IServiceCollection services)
    {
        services.TryAddSingleton<ILiveViewActivityStore, InMemoryActivityStore>();
        services.TryAddSingleton<IBackofficeMessageStore, InMemoryBackofficeMessageStore>();
        services.TryAddSingleton<ILiveActivityTracker, LiveActivityTracker>();
    }
}
