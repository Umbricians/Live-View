using Umbraco.Cms.Core.Models;

namespace Umbricians.LiveView.Presence;

/// <summary>
/// Supplies the picture shown next to a member in the dashboard's members table. Register your own to replace the
/// built-in one, which reads the property named in <c>Umbricians:LiveView:MemberAvatarProperty</c>.
/// </summary>
/// <remarks>
/// Called for each member on the page being shown, every time the table refreshes, so keep it quick. It has two
/// seconds; a member whose provider throws or runs out of time is shown with their initials.
/// </remarks>
public interface IMemberAvatarProvider
{
    /// <summary>A URL the backoffice can load as an image, ideally small and square, or null to show initials.</summary>
    Task<string?> GetAvatarUrlAsync(IMember member, CancellationToken cancellationToken);
}
