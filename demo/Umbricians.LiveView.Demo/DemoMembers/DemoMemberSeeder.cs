using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbricians.LiveView.Extensions;

namespace Umbricians.LiveView.Demo.DemoMembers;

/// <summary>
/// Creates the test members on startup if they do not exist yet. Demo only: not part of the package.
/// </summary>
public class DemoMemberSeeder : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string MemberTypeAlias = "Member";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRuntimeState _runtimeState;
    private readonly IOptions<DemoMembersOptions> _options;
    private readonly ILogger<DemoMemberSeeder> _logger;

    public DemoMemberSeeder(
        IServiceScopeFactory scopeFactory,
        IRuntimeState runtimeState,
        IOptions<DemoMembersOptions> options,
        ILogger<DemoMemberSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _runtimeState = runtimeState;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        DemoMembersOptions options = _options.Value;
        if (_runtimeState.Level != RuntimeLevel.Run || string.IsNullOrEmpty(options.Password))
        {
            return;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        IMemberManager memberManager = scope.ServiceProvider.GetRequiredService<IMemberManager>();

        int created = 0;
        foreach ((string email, string name) in options.Accounts)
        {
            if (await memberManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var member = MemberIdentityUser.CreateNew(email, email, MemberTypeAlias, isApproved: true, name);
            var result = await memberManager.CreateAsync(member, options.Password);
            if (result.Succeeded)
            {
                created++;
            }
            else
            {
                _logger.LogWarning(
                    "Could not create demo member {Email}: {Errors}",
                    email,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        if (created > 0)
        {
            _logger.LogInformation("Created {Count} demo members", created);
        }
    }
}

public class DemoMembersComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<DemoMembersOptions>(builder.Config.GetSection("LiveViewDemo:Members"));
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, DemoMemberSeeder>();

        // Sample member message senders for the Live View dashboard; the hub is mapped in Program.cs.
        builder.Services.AddSignalR();
        builder.AddMemberMessageSender<SignalRMemberMessageSender>();
        builder.AddMemberMessageSender<BannerMemberMessageSender>();
    }
}
