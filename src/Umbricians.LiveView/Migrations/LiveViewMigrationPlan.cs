using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Umbricians.LiveView.Migrations;

/// <summary>
/// Umbraco runs each step once per site, on startup, and records that it has, so later changes made by
/// administrators are never overwritten.
/// </summary>
public class LiveViewMigrationPlan : PackageMigrationPlan
{
    public LiveViewMigrationPlan()
        : base("Umbricians.LiveView")
    {
    }

    protected override void DefinePlan() =>
        To<GrantLiveViewPermissionsToAdministrators>("8f4b6a1e-2c3d-4e5f-9a0b-1c2d3e4f5a60");
}

/// <summary>
/// Gives the Administrators group all Live View permissions, so the dashboard is available straight after
/// installing or upgrading. Other groups are left for administrators to grant.
/// </summary>
public class GrantLiveViewPermissionsToAdministrators : AsyncMigrationBase
{
    private readonly IUserGroupService _userGroupService;

    public GrantLiveViewPermissionsToAdministrators(IMigrationContext context, IUserGroupService userGroupService)
        : base(context) => _userGroupService = userGroupService;

    protected override async Task MigrateAsync()
    {
        IUserGroup? administrators = await _userGroupService.GetAsync(Umbraco.Cms.Core.Constants.Security.AdminGroupAlias);
        if (administrators is null)
        {
            return;
        }

        bool changed = false;
        foreach (string permission in Constants.Permissions.All)
        {
            changed |= administrators.Permissions.Add(permission);
        }

        if (changed)
        {
            await _userGroupService.UpdateAsync(administrators, Umbraco.Cms.Core.Constants.Security.SuperUserKey);
        }
    }
}
