using Microsoft.Extensions.Logging;

namespace Umbricians.LiveView.Presence;

/// <summary>Runs a site's avatar lookup so that a failing or slow one costs the member their picture, never the table.</summary>
internal static class AvatarLookup
{
    public static async Task<string?> TryAsync(
        Func<CancellationToken, Task<string?>> lookup, TimeSpan timeout, ILogger logger, Guid memberKey)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            return await lookup(cancellation.Token).WaitAsync(cancellation.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live View could not get the avatar for member {MemberKey}", memberKey);
            return null;
        }
    }
}
