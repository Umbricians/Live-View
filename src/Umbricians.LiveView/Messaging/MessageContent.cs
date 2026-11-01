namespace Umbricians.LiveView.Messaging;

/// <summary>
/// What every message says, to backoffice users or members alike: a headline, an optional message and its type,
/// given as a colour. Text is trimmed and must fit the limits; an unknown colour is <c>default</c>.
/// </summary>
internal sealed record MessageContent(string Headline, string Message, string Color)
{
    internal const int MaxHeadlineLength = 200;
    internal const int MaxMessageLength = 2000;

    internal static readonly string[] Colors = ["default", "positive", "warning", "danger"];

    /// <summary>Why <see cref="TryCreate"/> turned the text down.</summary>
    internal static readonly string Limits =
        $"A headline (up to {MaxHeadlineLength} characters) is required, and the message can be up to {MaxMessageLength}.";

    /// <summary>The content as it is sent, or false when the headline is missing or either text is too long.</summary>
    internal static bool TryCreate(string? headline, string? message, string? color, out MessageContent content)
    {
        content = new MessageContent(
            headline?.Trim() ?? string.Empty,
            message?.Trim() ?? string.Empty,
            color is { } c && Colors.Contains(c) ? c : Colors[0]);
        return content.Headline.Length is > 0 and <= MaxHeadlineLength && content.Message.Length <= MaxMessageLength;
    }
}
