using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.HealthChecks;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>What the Member avatar property check says for each way MemberAvatarProperty can be set up.</summary>
public class MemberAvatarPropertyCheckTests
{
    [Fact]
    public void A_property_that_is_set_but_unused_because_the_site_has_its_own_provider_is_info()
    {
        HealthCheckStatus status = MemberAvatarPropertyCheck.Status("avatar", siteProvider: true, []);

        Assert.Equal(StatusResultType.Info, status.ResultType);
        Assert.Contains("'avatar'", status.Message);
    }

    [Fact]
    public void No_property_and_a_site_provider_is_a_success()
    {
        Assert.Equal(StatusResultType.Success, MemberAvatarPropertyCheck.Status(null, siteProvider: true, []).ResultType);
    }

    [Fact]
    public void No_property_is_a_success_with_initials()
    {
        HealthCheckStatus status = MemberAvatarPropertyCheck.Status(null, siteProvider: false, []);

        Assert.Equal(StatusResultType.Success, status.ResultType);
        Assert.Contains("initials", status.Message);
    }

    [Fact]
    public void A_property_found_on_a_member_type_is_a_success_naming_the_type()
    {
        HealthCheckStatus status = MemberAvatarPropertyCheck.Status("avatar", siteProvider: false, ["Member"]);

        Assert.Equal(StatusResultType.Success, status.ResultType);
        Assert.Contains("Member", status.Message);
    }

    [Fact]
    public void A_property_no_member_type_has_is_a_warning_naming_the_alias()
    {
        HealthCheckStatus status = MemberAvatarPropertyCheck.Status("nope", siteProvider: false, []);

        Assert.Equal(StatusResultType.Warning, status.ResultType);
        Assert.Contains("'nope'", status.Message);
    }
}
