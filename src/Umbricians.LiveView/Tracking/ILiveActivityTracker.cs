namespace Umbricians.LiveView.Tracking;

/// <summary>
/// Holds the in-memory set of recently active sessions per channel. Nothing is persisted.
/// </summary>
/// <remarks>
/// Request-tracked channels are fed by middleware. Inject this service to feed a channel from your own code,
/// for example an in-process MCP server or a SignalR hub that knows exactly when a session starts and ends.
/// </remarks>
public interface ILiveActivityTracker
{
    /// <summary>Marks a session as active now. The key is hashed before it is stored.</summary>
    void Touch(string channel, string sessionKey);

    /// <summary>
    /// Marks a session as active until the returned handle is disposed, then for the activity window after.
    /// Use this for long-lived connections such as server-sent event streams or WebSockets.
    /// </summary>
    IDisposable BeginActivity(string channel, string sessionKey);

    /// <summary>Removes a session straight away, e.g. when a client explicitly disconnects.</summary>
    void End(string channel, string sessionKey);

    /// <summary>Number of sessions in the channel that are connected or were seen within its activity window.</summary>
    int CountActive(string channel);
}
