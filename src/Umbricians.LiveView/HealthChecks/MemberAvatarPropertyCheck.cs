using System.Net;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Services;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.HealthChecks;

/// <summary>Checks that the member property named in <c>MemberAvatarProperty</c> exists on a member type.</summary>
[HealthCheck(
    "d75a1c48-9e3b-4062-a8f1-5c2e7b0d9a16",
    "Member avatar property",
    Description = "The property named in MemberAvatarProperty should exist on a member type.",
    Group = LiveViewHealthChecks.Group)]
internal sealed class MemberAvatarPropertyCheck : HealthCheck
{
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly IMemberAvatarProvider _avatars;
    private readonly IMemberTypeService _memberTypes;

    public MemberAvatarPropertyCheck(
        IOptionsMonitor<LiveViewOptions> options,
        IMemberAvatarProvider avatars,
        IMemberTypeService memberTypes)
    {
        _options = options;
        _avatars = avatars;
        _memberTypes = memberTypes;
    }

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        string? alias = _options.CurrentValue.MemberAvatarProperty;
        List<string> typesWithProperty = string.IsNullOrWhiteSpace(alias)
            ? []
            : _memberTypes.GetAll()
                .Where(t => t.CompositionPropertyTypes.Any(p => string.Equals(p.Alias, alias, StringComparison.OrdinalIgnoreCase)))
                .Select(t => t.Name ?? t.Alias)
                .ToList();

        return Task.FromResult<IEnumerable<HealthCheckStatus>>(
            [Status(alias, _avatars is not PropertyMemberAvatarProvider, typesWithProperty)]);
    }

    internal static HealthCheckStatus Status(
        string? propertyAlias,
        bool siteProvider,
        IReadOnlyCollection<string> memberTypesWithProperty)
    {
        bool aliasSet = !string.IsNullOrWhiteSpace(propertyAlias);
        string alias = WebUtility.HtmlEncode(propertyAlias ?? "");

        if (aliasSet && siteProvider)
        {
            return new HealthCheckStatus(
                $"<code>MemberAvatarProperty</code> is set to '{alias}' but isn't used: the site registers its own member avatar provider.")
            {
                ResultType = StatusResultType.Info,
            };
        }

        if (siteProvider)
        {
            return new HealthCheckStatus("Member pictures come from the site's own avatar provider.")
            {
                ResultType = StatusResultType.Success,
            };
        }

        if (!aliasSet)
        {
            return new HealthCheckStatus("No <code>MemberAvatarProperty</code> is set, so members are shown with their initials.")
            {
                ResultType = StatusResultType.Success,
            };
        }

        if (memberTypesWithProperty.Count > 0)
        {
            string types = string.Join(", ", memberTypesWithProperty.Select(WebUtility.HtmlEncode));
            return new HealthCheckStatus($"Member pictures come from the '{alias}' property on {types}.")
            {
                ResultType = StatusResultType.Success,
            };
        }

        return new HealthCheckStatus(
            $"No member type has a property with the alias '{alias}', so every member is shown with their initials. "
            + "Add an image property (media picker, image cropper or upload) with that alias to your member type, or correct <code>Umbricians:LiveView:MemberAvatarProperty</code>.")
        {
            ResultType = StatusResultType.Warning,
            ReadMoreLink = LiveViewHealthChecks.MemberAvatarsLink,
        };
    }
}
