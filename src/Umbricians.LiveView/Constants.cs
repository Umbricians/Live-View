namespace Umbricians.LiveView;

public static class Constants
{
    public const string ConfigurationSection = "Umbricians:LiveView";

    public static class Channels
    {
        public const string Web = "web";
        public const string Mcp = "mcp";
        public const string Api = "api";
        public const string Backoffice = "backoffice";
        public const string Members = "members";
    }

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
