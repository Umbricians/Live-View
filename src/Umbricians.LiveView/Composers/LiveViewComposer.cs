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
using Umbricians.LiveView.Metrics;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Composers;

public class LiveViewComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<LiveViewOptions>().Bind(builder.Config.GetSection(Constants.ConfigurationSection));
        builder.Services.AddSingleton<IPostConfigureOptions<LiveViewOptions>, LiveViewOptionsDefaults>();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ILiveActivityTracker, LiveActivityTracker>();
        builder.Services.AddSingleton<LiveActivityMiddleware>();
        builder.Services.AddSingleton<LiveMetricsService>();
        builder.Services.AddSingleton<BackofficePresence>();
        builder.Services.AddSingleton<MemberPresence>();
        builder.AddNotificationHandler<MemberLogoutSuccessNotification, MemberLogoutHandler>();
        builder.Services.AddSingleton<BackofficeMessenger>();

        builder.Services.Configure<UmbracoPipelineOptions>(options =>
            options.AddFilter(new UmbracoPipelineFilter("Umbricians.LiveView")
            {
                PrePipeline = app => app.UseMiddleware<LiveActivityMiddleware>(),
            }));
    }
}
