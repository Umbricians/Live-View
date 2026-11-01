using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Media;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Presence;

/// <summary>
/// The built-in <see cref="IMemberAvatarProvider"/>: the picture in the member property named by
/// <see cref="LiveViewOptions.MemberAvatarProperty"/>, cropped to a small square.
/// </summary>
internal sealed class PropertyMemberAvatarProvider : IMemberAvatarProvider
{
    private const int Size = 64;

    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly IPublishedMemberCache _memberCache;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly IImageUrlGenerator _imageUrlGenerator;

    public PropertyMemberAvatarProvider(
        IOptionsMonitor<LiveViewOptions> options,
        IPublishedMemberCache memberCache,
        IPublishedUrlProvider urlProvider,
        IImageUrlGenerator imageUrlGenerator)
    {
        _options = options;
        _memberCache = memberCache;
        _urlProvider = urlProvider;
        _imageUrlGenerator = imageUrlGenerator;
    }

    public async Task<string?> GetAvatarUrlAsync(IMember member, CancellationToken cancellationToken)
    {
        if (_options.CurrentValue.MemberAvatarProperty is not { Length: > 0 } alias)
        {
            return null;
        }

        IPublishedContent? published = await _memberCache.GetAsync(member);
        string? url = ImageUrl(published?.GetProperty(alias)?.GetValue());

        return string.IsNullOrEmpty(url)
            ? null
            : _imageUrlGenerator.GetImageUrl(new ImageUrlGenerationOptions(url)
            {
                Width = Size,
                Height = Size,
                ImageCropMode = ImageCropMode.Crop,
            }) ?? url;
    }

    // Media pickers (single or multiple, with or without crops), image croppers and upload fields.
    private string? ImageUrl(object? value) => value switch
    {
        IPublishedContent media => _urlProvider.GetMediaUrl(media),
        IEnumerable<IPublishedContent> media => media.FirstOrDefault() is { } first ? _urlProvider.GetMediaUrl(first) : null,
        ImageCropperValue cropper => cropper.Src,
        string path => path,
        _ => null,
    };
}
