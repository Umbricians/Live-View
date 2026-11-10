using Umbraco.Cms.Core.Events;

namespace Umbricians.LiveView.Messaging;

/// <summary>Why a site's notification handler stopped a message, as the dialog shows it.</summary>
internal static class CancelledSend
{
    internal const string DefaultReason = "A handler on this site stopped the message from being sent.";

    /// <summary>The handlers' event messages joined together, or <see cref="DefaultReason"/> if they gave none.</summary>
    internal static string Reason(EventMessages messages)
    {
        string[] reasons = messages.GetAll()
            .Select(m => m.Message)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToArray();
        return reasons.Length == 0 ? DefaultReason : string.Join(" ", reasons);
    }
}
