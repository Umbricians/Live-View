using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Metrics;

internal sealed record LiveMetricReading(string Alias, string Name, string? Color, double? Value, string? Error);

/// <summary>
/// Takes a snapshot of every enabled channel and metric provider line.
/// </summary>
internal sealed class LiveMetricsService
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(2);

    private readonly ILiveActivityTracker _tracker;
    private readonly BackofficePresence _backofficePresence;
    private readonly MemberPresence _memberPresence;
    private readonly IEnumerable<ILiveMetricProvider> _providers;
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly ILogger<LiveMetricsService> _logger;

    public LiveMetricsService(
        ILiveActivityTracker tracker,
        BackofficePresence backofficePresence,
        MemberPresence memberPresence,
        IEnumerable<ILiveMetricProvider> providers,
        IOptionsMonitor<LiveViewOptions> options,
        ILogger<LiveMetricsService> logger)
    {
        _tracker = tracker;
        _backofficePresence = backofficePresence;
        _memberPresence = memberPresence;
        _providers = providers;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LiveMetricReading>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        LiveViewOptions options = _options.CurrentValue;
        var channels = new List<(int Order, LiveMetricReading Reading)>();
        foreach (ChannelSettings c in ChannelSettings.InOrder(options, _providers).Where(c => c.Enabled))
        {
            channels.Add((c.SortOrder, new LiveMetricReading(c.Alias, c.Name, c.Color, await CountActiveAsync(c.Alias), null)));
        }

        // A provider whose line is switched off is not asked for its value.
        var lines = await Task.WhenAll(_providers
            .Select(p => (Provider: p, Line: ChannelSettings.Of(options, p)))
            .Where(l => l.Line.Enabled)
            .Select(async l => (Order: l.Line.SortOrder, Reading: await ReadAsync(l.Provider, l.Line, cancellationToken))));

        return channels.Concat(lines)
            .OrderBy(r => r.Order)
            .Select(r => r.Reading)
            .ToList();
    }

    // The members and backoffice users lines come from their presence modules, the same sources as their tables.
    private async Task<int> CountActiveAsync(string channel) => channel switch
    {
        Constants.Channels.Members => await _memberPresence.CountOnlineAsync(),
        Constants.Channels.Backoffice => await _backofficePresence.CountOnlineAsync(),
        _ => await _tracker.CountActiveAsync(channel),
    };

    private async Task<LiveMetricReading> ReadAsync(ILiveMetricProvider provider, ChannelSettings line, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProviderTimeout);

        try
        {
            double value = await provider.GetValueAsync(timeout.Token).WaitAsync(timeout.Token);
            return new LiveMetricReading(line.Alias, line.Name, line.Color, value, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiveMetricReading(line.Alias, line.Name, line.Color, null, "Timed out");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Live View metric provider {Alias} failed", provider.Alias);
            return new LiveMetricReading(line.Alias, line.Name, line.Color, null, "Failed");
        }
    }
}
