using Microsoft.AspNetCore.Http;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Tracking;

/// <summary>
/// Counts each request as the <see cref="RequestClassifier"/> describes it. Sessions stay active for the length of
/// the request, so long-lived streams (MCP over SSE, for one) count while they are open.
/// </summary>
internal sealed class LiveActivityMiddleware : IMiddleware
{
    private readonly RequestClassifier _classifier;
    private readonly ILiveActivityTracker _tracker;
    private readonly BackofficePresence _backofficePresence;
    private readonly MemberPresence _memberPresence;
    private readonly McpCallers _mcpCallers;

    public LiveActivityMiddleware(
        RequestClassifier classifier,
        ILiveActivityTracker tracker,
        BackofficePresence backofficePresence,
        MemberPresence memberPresence,
        McpCallers mcpCallers)
    {
        _classifier = classifier;
        _tracker = tracker;
        _backofficePresence = backofficePresence;
        _memberPresence = memberPresence;
        _mcpCallers = mcpCallers;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        RequestClassification request = _classifier.Classify(context.Request);
        if (!request.IsTracked)
        {
            await next(context);
            return;
        }

        await using (request.McpCall ? _mcpCallers.Begin(context) : null)
        {
            List<IDisposable> activities = request.Sessions.Select(s => _tracker.BeginActivity(s.Channel, s.SessionKey)).ToList();
            try
            {
                await next(context);
            }
            finally
            {
                activities.ForEach(a => a.Dispose());
            }
        }

        // The signed-in user (or member) is only known once authentication has run, so they are counted now.
        if (request.MemberSeen)
        {
            _memberPresence.Seen(context.User);
        }

        if (request.BackofficeSeen)
        {
            _backofficePresence.Seen(context.User);
        }

        foreach (ChannelSession session in request.SignedInSessions())
        {
            _tracker.Touch(session.Channel, session.SessionKey);
        }
    }
}
