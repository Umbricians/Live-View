namespace Umbricians.LiveView;

/// <summary>
/// Names a site uses to configure Live View or refer to its parts from code.
/// </summary>
public static class Constants
{
    /// <summary>The appsettings section Live View reads its options from.</summary>
    public const string ConfigurationSection = "Umbricians:LiveView";

    /// <summary>
    /// The name of the package's Management API document in the backoffice's Swagger UI.
    /// </summary>
    public const string ApiName = "umbricians-live-view";

    /// <summary>
    /// User group permissions, granted in Users → User groups → Permissions → Live View. They are stored with the
    /// group's other permissions; the backoffice registers matching <c>entityUserPermission</c> extensions. The dashboard's API checks these, not
    /// section access.
    /// </summary>
    public static class Permissions
    {
        /// <summary>See the Live View dashboard: live activity and who is online.</summary>
        public const string View = "Umbricians.LiveView.View";

        /// <summary>Send notifications to backoffice users who are online.</summary>
        public const string MessageUsers = "Umbricians.LiveView.MessageUsers";

        /// <summary>Send messages to website members through the site's member message senders.</summary>
        public const string MessageMembers = "Umbricians.LiveView.MessageMembers";

        /// <summary>Every Live View permission, for granting them all to a user group at once.</summary>
        public static IReadOnlyList<string> All { get; } = [View, MessageUsers, MessageMembers];
    }

    /// <summary>
    /// Aliases of the built-in channels, for configuring them under <c>Umbricians:LiveView:Channels</c>, restyling them
    /// with <see cref="Extensions.LiveViewBuilderExtensions.AddLiveViewChannel"/> or feeding them from code with
    /// <see cref="Tracking.ILiveActivityTracker"/>.
    /// </summary>
    public static class Channels
    {
        /// <summary>Website visitors, counted by client.</summary>
        public const string Web = "web";

        /// <summary>MCP clients calling the site's MCP endpoints.</summary>
        public const string Mcp = "mcp";

        /// <summary>Callers of the site's APIs, including the Delivery API.</summary>
        public const string Api = "api";

        /// <summary>Signed-in backoffice users.</summary>
        public const string Backoffice = "backoffice";

        /// <summary>Signed-in website members, who are counted as web visitors too.</summary>
        public const string Members = "members";
    }

    /// <summary>
    /// The values a channel's <see cref="Configuration.LiveViewChannelOptions.SessionKeys"/> can list, besides the alias
    /// of a session key source the site registers.
    /// </summary>
    public static class SessionKeySources
    {
        /// <summary>A hash of the client IP address and User-Agent. Sets no cookies.</summary>
        public const string Client = "Client";

        /// <summary>
        /// The signed-in user's id, e.g. a backoffice user. Resolved once the request has been authorized,
        /// so requests that fail authentication are not counted.
        /// </summary>
        public const string User = "User";

        /// <summary>A hash of the Authorization header (e.g. a bearer token).</summary>
        public const string Authorization = "Authorization";

        /// <summary>Prefix for a named request header, e.g. <c>Header:Mcp-Session-Id</c>.</summary>
        public const string HeaderPrefix = "Header:";

        /// <summary>Prefix for a named cookie, e.g. <c>Cookie:_ga</c>.</summary>
        public const string CookiePrefix = "Cookie:";
    }
}
