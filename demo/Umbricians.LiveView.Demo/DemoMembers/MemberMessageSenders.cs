using Microsoft.AspNetCore.SignalR;
using Umbraco.Cms.Core.Security;
using Umbricians.LiveView.Messaging.Members;

namespace Umbricians.LiveView.Demo.DemoMembers;

/// <summary>
/// Sample: a SignalR hub that member pages connect to, so messages can pop up on pages members already have open.
/// Demo only: not part of the package.
/// </summary>
public class MemberMessagesHub : Hub
{
    public const string Path = "/demo/hubs/member-messages";
    public const string AllMembersGroup = "members";

    private readonly IMemberManager _memberManager;

    public MemberMessagesHub(IMemberManager memberManager) => _memberManager = memberManager;

    public static string GroupFor(Guid memberKey) => $"member:{memberKey}";

    public override async Task OnConnectedAsync()
    {
        // Anonymous visitors can connect but join no group, so they never receive member messages.
        if (await _memberManager.GetCurrentMemberAsync() is { } member)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(member.Key));
            await Groups.AddToGroupAsync(Context.ConnectionId, AllMembersGroup);
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Sample sender: pushes the message to pages members have open. Reaches only members online right now.
/// </summary>
public class SignalRMemberMessageSender : IMemberMessageSender
{
    private readonly IHubContext<MemberMessagesHub> _hub;

    public SignalRMemberMessageSender(IHubContext<MemberMessagesHub> hub) => _hub = hub;

    public string Alias => "signalr";

    public string Name => "Live pop-up (SignalR)";

    public string Description => "Pops up straight away on any page members have open.";

    public async Task<MemberMessageResult> SendAsync(MemberMessage message, CancellationToken cancellationToken)
    {
        var payload = new
        {
            message.Key,
            message.Headline,
            message.Body,
            Type = message.Type.ToString(),
            message.SenderName,
        };

        if (message.Audience == MemberMessageAudience.AllMembers)
        {
            await _hub.Clients.Group(MemberMessagesHub.AllMembersGroup).SendAsync("message", payload, cancellationToken);
            return new MemberMessageResult(null, "every member with a page open");
        }

        await _hub.Clients
            .Groups(message.MemberKeys.Select(MemberMessagesHub.GroupFor).ToList())
            .SendAsync("message", payload, cancellationToken);
        return new MemberMessageResult(message.MemberKeys.Count, "if they have a page open");
    }
}

/// <summary>
/// Sample sender: queues the message in the package's <see cref="IMemberMessageInbox"/>; the demo members page
/// shows it as a banner until it is dismissed or expires. Reaches members who are offline too.
/// </summary>
public class BannerMemberMessageSender : IMemberMessageSender
{
    private readonly IMemberMessageInbox _inbox;

    public BannerMemberMessageSender(IMemberMessageInbox inbox) => _inbox = inbox;

    public string Alias => "banner";

    public string Name => "Banner on next page";

    public string Description => "Shown at the top of the next page each member views, until dismissed or it expires.";

    public bool ReachesOfflineMembers => true;

    public Task<MemberMessageResult> SendAsync(MemberMessage message, CancellationToken cancellationToken)
    {
        _inbox.Add(message);
        int? queuedFor = message.Audience == MemberMessageAudience.AllMembers ? null : message.MemberKeys.Count;
        return Task.FromResult(new MemberMessageResult(queuedFor, "queued for their next page view"));
    }
}
