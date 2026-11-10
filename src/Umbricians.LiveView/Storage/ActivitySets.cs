namespace Umbricians.LiveView.Storage;

/// <summary>The names of the sets Live View keeps in the <see cref="ILiveViewActivityStore"/>.</summary>
internal static class ActivitySets
{
    public const string BackofficeUsers = "backoffice-users";
    public const string BackofficeMcp = "backoffice-mcp";
    public const string Members = "members";
    public const string MemberMcp = "members-mcp";

    // Channels are case-insensitive, so their sets are too.
    public static string Channel(string alias) => "channel:" + alias.ToLowerInvariant();

    public static string BackofficeTabs(Guid user) => $"backoffice-tabs:{user:N}";

    /// <summary>The key of a person in the sets that hold people.</summary>
    public static string Person(Guid key) => key.ToString("N");
}
