using System.Net;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Services;
using Umbricians.LiveView.Messaging.Members;

namespace Umbricians.LiveView.HealthChecks;

/// <summary>Checks that user groups allowed to message members have a member message sender to deliver with.</summary>
[HealthCheck(
    "4e8d2b71-c0a6-4f39-b5d2-7a1e6c9f3d04",
    "Member messaging",
    Description = "User groups with the Message members permission need a member message sender to deliver their messages.",
    Group = LiveViewHealthChecks.Group)]
internal sealed class MemberMessagingCheck : HealthCheck
{
    private readonly IEnumerable<IMemberMessageSender> _senders;
    private readonly IUserGroupService _userGroups;

    public MemberMessagingCheck(IEnumerable<IMemberMessageSender> senders, IUserGroupService userGroups)
    {
        _senders = senders;
        _userGroups = userGroups;
    }

    public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        List<(Guid Key, string Name)> groups = (await _userGroups.GetAllAsync(0, int.MaxValue)).Items
            .Where(g => g.Permissions.Contains(Constants.Permissions.MessageMembers))
            .Select(g => (g.Key, g.Name ?? g.Alias))
            .ToList();

        return [Status(_senders.Select(s => s.Name).ToList(), groups)];
    }

    internal static HealthCheckStatus Status(
        IReadOnlyCollection<string> senders,
        IReadOnlyCollection<(Guid Key, string Name)> groupsWithPermission)
    {
        string groupNames = string.Join(", ", groupsWithPermission.Select(g => WebUtility.HtmlEncode(g.Name)));

        if (senders.Count > 0 && groupsWithPermission.Count > 0)
        {
            string senderNames = string.Join(", ", senders.Select(WebUtility.HtmlEncode));
            return new HealthCheckStatus($"Members can be messaged by {groupNames}, through {senderNames}.")
            {
                ResultType = StatusResultType.Success,
            };
        }

        if (senders.Count > 0)
        {
            return new HealthCheckStatus(
                $"{senders.Count} member message sender(s) are registered, but no user group has the Message members permission, so nobody can message members. "
                + "Grant it under Users → User groups.")
            {
                ResultType = StatusResultType.Info,
                ReadMoreLink = LiveViewHealthChecks.MemberMessagingLink,
            };
        }

        // Administrators get the permission automatically on install, so on their own they say nothing about the site.
        if (groupsWithPermission.Any(g => g.Key != Umbraco.Cms.Core.Constants.Security.AdminGroupKey))
        {
            return new HealthCheckStatus(
                $"{groupNames} have the Message members permission, but no member message sender is registered, so they can't message members. "
                + "Register one with <code>builder.AddLiveViewMemberMessageSender&lt;T&gt;()</code>, or remove the permission.")
            {
                ResultType = StatusResultType.Warning,
                ReadMoreLink = LiveViewHealthChecks.MemberMessagingLink,
            };
        }

        return new HealthCheckStatus("Member messaging is off: no member message sender is registered.")
        {
            ResultType = StatusResultType.Info,
        };
    }
}
