using Umbraco.Cms.Web.Common.Authorization;

namespace Umbricians.LiveView.Demo.DemoMcp;

/// <summary>
/// A stand-in for an MCP endpoint hosted by the site, so the dashboard's MCP rows can be tried. It is not an MCP
/// server: it only answers under <c>/mcp</c>, where Live View counts MCP sessions, as a signed-in member or
/// backoffice user. Demo only: not part of the package.
/// </summary>
public static class DemoMcpEndpoints
{
    public static void MapDemoMcp(this IEndpointRouteBuilder endpoints)
    {
        // Members sign in with the website's member cookie.
        RouteGroupBuilder member = endpoints.MapGroup("/mcp/member").RequireAuthorization(p => p.RequireAuthenticatedUser());
        member.MapPost("", (HttpContext context) => Answer(context));
        member.MapGet("/stream", Stream);

        // Backoffice users sign in with a backoffice access token.
        RouteGroupBuilder backoffice = endpoints.MapGroup("/mcp/backoffice").RequireAuthorization(AuthorizationPolicies.BackOfficeAccess);
        backoffice.MapPost("", (HttpContext context) => Answer(context));
        backoffice.MapGet("/stream", Stream);
    }

    private static IResult Answer(HttpContext context) =>
        Results.Json(new { jsonrpc = "2.0", result = new { caller = context.User.Identity?.Name } });

    /// <summary>Holds an event stream open for <paramref name="seconds"/>, like an MCP client's notification stream.</summary>
    private static async Task Stream(HttpContext context, int seconds = 30)
    {
        context.Response.ContentType = "text/event-stream";
        DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(seconds, 1, 600));
        while (DateTimeOffset.UtcNow < until && !context.RequestAborted.IsCancellationRequested)
        {
            await context.Response.WriteAsync("event: ping\ndata: {}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await Task.Delay(TimeSpan.FromSeconds(2), context.RequestAborted).ContinueWith(_ => { });
        }
    }
}
