using Microsoft.AspNetCore.Http;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Tracking;

/// <summary>
/// Counts each request as the <see cref="RequestClassifier"/> describes it. Sessions stay active for the length of
/// the request, so long-lived streams (MCP over SSE, for one) count while they are open. Also notes requests from a
/// proxy whose forwarded headers weren't applied.
/// </summary>
internal sealed class LiveActivityMiddleware : IMiddleware
{
    private readonly RequestClassifier _classifier;
    private readonly ILiveActivityTracker _tracker;
    private readonly BackofficePresence _backofficePresence;
    private readonly MemberPresence _memberPresence;
    private readonly McpCallers _mcpCallers;
    private readonly UnforwardedProxyDetector _proxyDetector;

    public LiveActivityMiddleware(
        RequestClassifier classifier,
        ILiveActivityTracker tracker,
        BackofficePresence backofficePresence,
        MemberPresence memberPresence,
        McpCallers mcpCallers,
        UnforwardedProxyDetector proxyDetector)
    {
        _classifier = classifier;
        _tracker = tracker;
        _backofficePresence = backofficePresence;
        _memberPresence = memberPresence;
        _mcpCallers = mcpCallers;
        _proxyDetector = proxyDetector;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        _proxyDetector.Observe(context);

        RequestClassification request = _classifier.Classify(context.Request);
        if (!request.IsTracked)
        {
            await next(context);
            return;
        }

        await using (request.McpCall ? _mcpCallers.Begin(context) : null)
        {
            var activities = new List<IAsyncDisposable>();
            try
            {
                foreach (ChannelSession session in request.Sessions)
                {
                    activities.Add(await _tracker.BeginActivityAsync(session.Channel, session.SessionKey));
                }

                await next(context);
            }
            finally
            {
                foreach (IAsyncDisposable activity in activities)
                {
                    await activity.DisposeAsync();
                }
            }
        }

        // The signed-in user (or member) is only known once authentication has run, so they are counted now.
        if (request.MemberSeen)
        {
            await _memberPresence.SeenAsync(context.User);
        }

        if (request.BackofficeSeen)
        {
            await _backofficePresence.SeenAsync(context.User);
        }

        foreach (ChannelSession session in request.SignedInSessions())
        {
            await _tracker.TouchAsync(session.Channel, session.SessionKey);
        }
    }
}
