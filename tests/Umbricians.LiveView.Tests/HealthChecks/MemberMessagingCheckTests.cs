using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.HealthChecks;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>What the Member messaging check says about senders and the groups allowed to use them.</summary>
public class MemberMessagingCheckTests
{
    private static readonly (Guid Key, string Name) Editors = (Guid.NewGuid(), "Editors");
    private static readonly (Guid Key, string Name) Administrators = (Umbraco.Cms.Core.Constants.Security.AdminGroupKey, "Administrators");

    [Test]
    public void Senders_and_a_group_with_the_permission_is_a_success()
    {
        HealthCheckStatus status = MemberMessagingCheck.Status(["Banner on next page"], [Editors]);

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Success));
        Assert.That(status.Message, Does.Contain("Editors"));
        Assert.That(status.Message, Does.Contain("Banner on next page"));
    }

    [Test]
    public void Senders_without_any_group_is_info()
    {
        HealthCheckStatus status = MemberMessagingCheck.Status(["Banner on next page"], []);

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Info));
        Assert.That(status.Message, Does.Contain("no user group"));
    }

    [Test]
    public void A_group_with_the_permission_but_no_senders_is_a_warning_naming_the_group()
    {
        HealthCheckStatus status = MemberMessagingCheck.Status([], [Administrators, Editors]);

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Warning));
        Assert.That(status.Message, Does.Contain("Editors"));
    }

    [Test]
    public void Only_administrators_with_the_permission_and_no_senders_is_info()
    {
        Assert.That(MemberMessagingCheck.Status([], [Administrators]).ResultType, Is.EqualTo(StatusResultType.Info));
    }

    // Administrators is recognised by its key, which never changes, not by its name or alias, which can.
    [Test]
    public void A_group_only_named_like_administrators_with_the_permission_and_no_senders_is_a_warning()
    {
        HealthCheckStatus status = MemberMessagingCheck.Status([], [(Guid.NewGuid(), "Administrators")]);

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Warning));
    }

    [Test]
    public void No_senders_and_no_groups_is_info()
    {
        Assert.That(MemberMessagingCheck.Status([], []).ResultType, Is.EqualTo(StatusResultType.Info));
    }

    [Test]
    public void Group_names_are_html_encoded()
    {
        HealthCheckStatus status = MemberMessagingCheck.Status([], [(Guid.NewGuid(), "<b>Editors</b>")]);

        Assert.That(status.Message, Does.Contain("&lt;b&gt;"));
        Assert.That(status.Message, Does.Not.Contain("<b>Editors"));
    }
}
