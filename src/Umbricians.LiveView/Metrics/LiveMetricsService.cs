using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Metrics;

public sealed record LiveMetricReading(string Alias, string Name, string? Color, double? Value, string? Error);

/// <summary>
/// Takes a snapshot of every enabled tracked channel and registered <see cref="ILiveMetricProvider"/>.
/// </summary>
public sealed class LiveMetricsService
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
        var channels = _options.CurrentValue.Channels
            .Where(c => c.Value.Enabled)
            .Select(c => (Order: c.Value.SortOrder ?? 0, Reading: new LiveMetricReading(
                c.Key, c.Value.Name ?? c.Key, c.Value.Color, CountActive(c.Key), null)));

        var providers = await Task.WhenAll(_providers.Select(async p =>
            (Order: p.SortOrder, Reading: await ReadAsync(p, cancellationToken))));

        return channels.Concat(providers)
            .OrderBy(r => r.Order)
            .Select(r => r.Reading)
            .ToList();
    }

    // The members and backoffice users lines come from their presence modules, the same sources as their tables.
    private int CountActive(string channel) => channel switch
    {
        Constants.Channels.Members => _memberPresence.CountOnline(),
        Constants.Channels.Backoffice => _backofficePresence.CountOnline(),
        _ => _tracker.CountActive(channel),
    };

    private async Task<LiveMetricReading> ReadAsync(ILiveMetricProvider provider, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProviderTimeout);

        try
        {
            double value = await provider.GetValueAsync(timeout.Token).WaitAsync(timeout.Token);
            return new LiveMetricReading(provider.Alias, provider.Name, provider.Color, value, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiveMetricReading(provider.Alias, provider.Name, provider.Color, null, "Timed out");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Live View metric provider {Alias} failed", provider.Alias);
            return new LiveMetricReading(provider.Alias, provider.Name, provider.Color, null, "Failed");
        }
    }
}
