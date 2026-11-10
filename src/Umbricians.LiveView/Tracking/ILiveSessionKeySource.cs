using Microsoft.AspNetCore.Http;

namespace Umbricians.LiveView.Tracking;

/// <summary>
/// A session key source the site works out in its own code, such as a claim, a tenant id or an API client id. A
/// channel uses it by listing its <see cref="Alias"/> in <c>SessionKeys</c>, alongside or instead of the built-in sources.
/// </summary>
/// <remarks>
/// Register with <c>builder.AddLiveViewSessionKeySource&lt;T&gt;()</c>. Sources are singletons and are called on every
/// request a channel using them counts, so keep <see cref="GetSessionKey"/> quick. Values are hashed before they are
/// held in memory. The built-in source names (<c>User</c>, <c>Client</c>, <c>Authorization</c>, <c>Header:…</c>,
/// <c>Cookie:…</c>) always mean the built-in sources.
/// </remarks>
public interface ILiveSessionKeySource
{
    /// <summary>The name a channel's <c>SessionKeys</c> uses for this source, matched without regard to case.</summary>
    string Alias { get; }

    /// <summary>
    /// Whether the key comes from the signed-in user, such as one of their claims. The source is then read once
    /// authentication has run, so requests that fail authentication are not counted.
    /// </summary>
    bool UsesSignedInUser => false;

    /// <summary>The session's identity for this request, or null or empty when it has none, so the next source is tried.</summary>
    string? GetSessionKey(HttpContext context);
}
