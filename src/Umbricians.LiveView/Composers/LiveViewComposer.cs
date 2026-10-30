using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Metrics;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Composers;

public class LiveViewComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<LiveViewOptions>().Bind(builder.Config.GetSection(Constants.ConfigurationSection));
        LiveViewOptionsDefaults.AddTo(builder.Services);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ILiveActivityTracker, LiveActivityTracker>();
        builder.Services.AddSingleton<LiveActivityMiddleware>();
        builder.Services.AddSingleton<LiveMetricsService>();
        builder.Services.AddSingleton<BackofficePresence>();
        builder.Services.AddSingleton<IMemberDirectory, UmbracoMemberDirectory>();
        builder.Services.AddSingleton(sp => new MemberPresence(
            sp.GetRequiredService<IMemberDirectory>(),
            sp.GetRequiredService<IOptionsMonitor<LiveViewOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.AddNotificationHandler<MemberLogoutSuccessNotification, MemberLogoutHandler>();
        builder.AddNotificationHandler<MemberDeletedNotification, MemberDeletedHandler>();
        builder.Services.AddSingleton<BackofficeMessenger>();
        builder.Services.AddSingleton<MemberMessagingService>();
        builder.Services.TryAddSingleton<IMemberMessageInbox, MemberMessageInbox>();

        builder.Services.Configure<UmbracoPipelineOptions>(options =>
            options.AddFilter(new UmbracoPipelineFilter("Umbricians.LiveView")
            {
                PrePipeline = app => app.UseMiddleware<LiveActivityMiddleware>(),
            }));
    }
}
