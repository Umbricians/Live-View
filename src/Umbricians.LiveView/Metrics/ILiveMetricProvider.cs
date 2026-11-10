namespace Umbricians.LiveView.Metrics;

/// <summary>
/// A single live line on the Live View dashboard. Implement this to chart any number the site can work out
/// on demand, such as connections reported by an out-of-process MCP server or a queue length.
/// </summary>
/// <remarks>
/// Register with <c>builder.AddLiveViewMetric&lt;T&gt;()</c>. Providers are singletons and are called every time a
/// dashboard polls, so keep <see cref="GetValueAsync"/> fast; slow or failing providers are reported as gaps.
/// Settings under <c>Umbricians:LiveView:Channels:{Alias}</c> in appsettings apply to the line: <c>Enabled</c> switches
/// it off, so the provider is not called, and <c>Name</c>, <c>Color</c> and <c>SortOrder</c> replace this provider's own values.
/// </remarks>
public interface ILiveMetricProvider
{
    /// <summary>
    /// Unique, stable key for the line. A provider whose alias matches a channel, built-in or declared in code,
    /// replaces that channel: the channel stops counting requests, and settings under the alias apply to this line.
    /// </summary>
    string Alias { get; }

    /// <summary>Label shown in the legend.</summary>
    string Name { get; }

    /// <summary>Line colour, any CSS colour. Null picks one from the dashboard palette.</summary>
    string? Color => null;

    /// <summary>Lines are listed in ascending order.</summary>
    int SortOrder => 1000;

    Task<double> GetValueAsync(CancellationToken cancellationToken);
}
