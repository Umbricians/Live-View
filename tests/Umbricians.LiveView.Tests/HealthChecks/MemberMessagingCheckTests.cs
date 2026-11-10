using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.HealthChecks;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>What the Member messaging check says about senders and the groups allowed to use them.</summary>
public class MemberMessagingCheckTests
{
    private static readonly (string Alias, string Name) Editors = ("editors", "Editors");
    private static readonly (string Alias, string Name) Administrators = ("admin", "Administrators");

    [Fact]
    public void Senders_and_a_group_with_the_permission_is_a_success()
    {
        HealthCheckStatus status = MemberMessagingCheck.Status(["Banner on next page"], [Editors]);

        Assert.Equal(StatusResultType.Success, status.ResultType);
        Assert.Contains("Editors", status.Message);
        Assert.Contains("Banner on next page", status.Message);
    }

    [Fact]
    public void Senders_without_any_group_is_info()
    {
        HealthCheckStatus status = MemberMessagingCheck.Status(["Banner on next page"], []);

        Assert.Equal(StatusResultType.Info, status.ResultType);
        Assert.Contains("no user group", status.Message);
    }

    [Fact]
    public void A_group_with_the_permission_but_no_senders_is_a_warning_naming_the_group()
    {
        HealthCheckStatus status = MemberMessagingCheck.Status([], [Administrators, Editors]);

        Assert.Equal(StatusResultType.Warning, status.ResultType);
        Assert.Contains("Editors", status.Message);
    }

    [Fact]
    public void Only_administrators_with_the_permission_and_no_senders_is_info()
    {
        Assert.Equal(StatusResultType.Info, MemberMessagingCheck.Status([], [Administrators]).ResultType);
    }

    [Fact]
    public void No_senders_and_no_groups_is_info()
    {
        Assert.Equal(StatusResultType.Info, MemberMessagingCheck.Status([], []).ResultType);
    }

    [Fact]
    public void Group_names_are_html_encoded()
    {
        HealthCheckStatus status = MemberMessagingCheck.Status([], [("x", "<b>Editors</b>")]);

        Assert.Contains("&lt;b&gt;", status.Message);
        Assert.DoesNotContain("<b>Editors", status.Message);
    }
}
