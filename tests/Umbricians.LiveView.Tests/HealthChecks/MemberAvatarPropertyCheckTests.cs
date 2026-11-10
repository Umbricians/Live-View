using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.HealthChecks;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>What the Member avatar property check says for each way MemberAvatarProperty can be set up.</summary>
public class MemberAvatarPropertyCheckTests
{
    [Test]
    public void A_property_that_is_set_but_unused_because_the_site_has_its_own_provider_is_info()
    {
        HealthCheckStatus status = MemberAvatarPropertyCheck.Status("avatar", siteProvider: true, []);

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Info));
        Assert.That(status.Message, Does.Contain("'avatar'"));
    }

    [Test]
    public void No_property_and_a_site_provider_is_a_success()
    {
        Assert.That(MemberAvatarPropertyCheck.Status(null, siteProvider: true, []).ResultType, Is.EqualTo(StatusResultType.Success));
    }

    [Test]
    public void No_property_is_a_success_with_initials()
    {
        HealthCheckStatus status = MemberAvatarPropertyCheck.Status(null, siteProvider: false, []);

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Success));
        Assert.That(status.Message, Does.Contain("initials"));
    }

    [Test]
    public void A_property_found_on_a_member_type_is_a_success_naming_the_type()
    {
        HealthCheckStatus status = MemberAvatarPropertyCheck.Status("avatar", siteProvider: false, ["Member"]);

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Success));
        Assert.That(status.Message, Does.Contain("Member"));
    }

    [Test]
    public void A_property_no_member_type_has_is_a_warning_naming_the_alias()
    {
        HealthCheckStatus status = MemberAvatarPropertyCheck.Status("nope", siteProvider: false, []);

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Warning));
        Assert.That(status.Message, Does.Contain("'nope'"));
    }
}
