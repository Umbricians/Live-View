using Microsoft.AspNetCore.Http;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Presence;

/// <summary>
/// Takes a member off the dashboard as soon as they log out, rather than when their activity window runs out.
/// </summary>
internal sealed class MemberLogoutHandler : INotificationHandler<MemberLogoutSuccessNotification>
{
    /// <summary>
    /// Set on the logout request itself, which is still made as the member, so the tracking middleware does not
    /// record them again as that request completes.
    /// </summary>
    internal const string SignedOutItemKey = "Umbricians.LiveView.MemberSignedOut";

    private readonly MemberPresence _presence;
    private readonly ILiveActivityTracker _tracker;
    private readonly IMemberService _memberService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MemberLogoutHandler(
        MemberPresence presence,
        ILiveActivityTracker tracker,
        IMemberService memberService,
        IHttpContextAccessor httpContextAccessor)
    {
        _presence = presence;
        _tracker = tracker;
        _memberService = memberService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void Handle(MemberLogoutSuccessNotification notification)
    {
        if (notification.MemberKey is not { } key)
        {
            return;
        }

        if (_httpContextAccessor.HttpContext is { } httpContext)
        {
            httpContext.Items[SignedOutItemKey] = true;
        }

        // Members are recorded by key where their claims carry one, otherwise by id, so forget both.
        string[] ids = _memberService.GetById(key) is { } member
            ? [key.ToString(), member.Id.ToString()]
            : [key.ToString()];

        foreach (string id in ids)
        {
            _presence.Remove(id);
            _tracker.End(Constants.Channels.Members, Constants.SessionKeySources.User + ":" + id);
        }
    }
}
