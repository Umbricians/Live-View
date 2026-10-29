using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbricians.LiveView.Extensions;
using Umbricians.LiveView.Metrics;

namespace Umbricians.LiveView.Demo.LiveView;

/// <summary>
/// Example of a custom line on the Live View dashboard: any number the site can work out on demand.
/// </summary>
public class ThreadPoolQueueMetric : ILiveMetricProvider
{
    public string Alias => "threadpool-queue";

    public string Name => "Thread pool queue";

    public string? Color => "#8b5cf6";

    public Task<double> GetValueAsync(CancellationToken cancellationToken) =>
        Task.FromResult((double)ThreadPool.PendingWorkItemCount);
}

public class LiveViewDemoComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.AddLiveViewMetric<ThreadPoolQueueMetric>();
}
