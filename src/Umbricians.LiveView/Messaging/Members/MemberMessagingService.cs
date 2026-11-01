using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Messaging.Members;

/// <summary>A member message as the dashboard sends it: text untrimmed, audience, senders and expiry unchecked.</summary>
internal sealed record MemberMessageRequest(
    string? Headline,
    string? Message,
    string? Color,
    string? Audience,
    IReadOnlyCollection<Guid>? MemberKeys,
    IReadOnlyCollection<string>? Senders,
    DateTimeOffset? ExpiresAt);

internal sealed record MemberMessageSenderResult(string Alias, string Name, int? Delivered, string? Note, string? Error);

internal sealed record MemberMessageSendResult(Guid Key, MemberMessageAudience Audience, int? Recipients, IReadOnlyList<MemberMessageSenderResult> Results);

internal abstract record MemberSendResult
{
    private MemberSendResult()
    {
    }

    internal sealed record Sent(MemberMessageSendResult Result) : MemberSendResult;

    internal sealed record Rejected(string Reason) : MemberSendResult;
}

/// <summary>
/// Checks a member message, works out who it is for, then hands it to each chosen <see cref="IMemberMessageSender"/>.
/// </summary>
/// <remarks>
/// It owns the send rules: the message content (see <see cref="MessageContent"/>), the audience, who "all online"
/// means at the moment of sending, the delivery methods and when the message stops being shown.
/// </remarks>
internal sealed class MemberMessagingService
{
    internal const int MaxSelectedMembers = 10_000;

    private static readonly TimeSpan SenderTimeout = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<string, MemberMessageType> Types = new()
    {
        ["default"] = MemberMessageType.Information,
        ["warning"] = MemberMessageType.Warning,
        ["danger"] = MemberMessageType.Urgent,
        ["positive"] = MemberMessageType.GoodNews,
    };

    private static readonly Dictionary<string, MemberMessageAudience> Audiences = new(StringComparer.OrdinalIgnoreCase)
    {
        ["selected"] = MemberMessageAudience.Selected,
        ["allOnline"] = MemberMessageAudience.AllOnline,
        ["allMembers"] = MemberMessageAudience.AllMembers,
    };

    private readonly IReadOnlyList<IMemberMessageSender> _senders;
    private readonly MemberPresence _presence;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MemberMessagingService> _logger;

    public MemberMessagingService(
        IEnumerable<IMemberMessageSender> senders,
        MemberPresence presence,
        TimeProvider timeProvider,
        ILogger<MemberMessagingService> logger)
    {
        _senders = senders.ToList();
        _presence = presence;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IReadOnlyList<IMemberMessageSender> Senders => _senders;

    /// <summary>Checks the message, works out its recipients and hands it to the chosen senders, or says why it can't be sent.</summary>
    public async Task<MemberSendResult> SendAsync(MemberMessageRequest request, string? senderName, CancellationToken cancellationToken)
    {
        if (!MessageContent.TryCreate(request.Headline, request.Message, request.Color, out MessageContent content))
        {
            return new MemberSendResult.Rejected(MessageContent.Limits);
        }

        if (!Audiences.TryGetValue(request.Audience ?? string.Empty, out MemberMessageAudience audience))
        {
            return new MemberSendResult.Rejected("Audience must be 'selected', 'allOnline' or 'allMembers'.");
        }

        Guid[] selected = request.MemberKeys?.Distinct().ToArray() ?? [];
        if (audience == MemberMessageAudience.Selected && selected.Length is 0 or > MaxSelectedMembers)
        {
            return new MemberSendResult.Rejected(
                $"Select between 1 and {MaxSelectedMembers} members, or send to all online members instead.");
        }

        IMemberMessageSender[] chosen = _senders
            .Where(s => request.Senders?.Contains(s.Alias, StringComparer.OrdinalIgnoreCase) == true)
            .ToArray();
        if (chosen.Length == 0)
        {
            return new MemberSendResult.Rejected("Choose at least one delivery method.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!MemberMessageExpiry.TryResolve(request.ExpiresAt, now, out DateTimeOffset expiresAt))
        {
            return new MemberSendResult.Rejected("Choose when the message stops being shown: a time in the future.");
        }

        // "All online" is resolved here, at the moment of sending, rather than from what the browser last loaded.
        IReadOnlyList<Guid> memberKeys = audience switch
        {
            MemberMessageAudience.Selected => selected,
            MemberMessageAudience.AllOnline => _presence.AllOnline(),
            _ => [],
        };

        var message = new MemberMessage(
            Guid.NewGuid(),
            content.Headline,
            content.Message,
            Types[content.Color],
            audience,
            memberKeys,
            senderName,
            now,
            expiresAt);

        MemberMessageSenderResult[] results = await Task.WhenAll(chosen.Select(s => SendWithAsync(s, message, cancellationToken)));

        _logger.LogInformation(
            "{SenderName} sent member message {MessageKey} to {Audience} ({Recipients}) via {Senders}",
            senderName,
            message.Key,
            message.Audience,
            message.Audience == MemberMessageAudience.AllMembers ? "every member" : $"{memberKeys.Count} members",
            string.Join(", ", chosen.Select(s => s.Alias)));

        return new MemberSendResult.Sent(new MemberMessageSendResult(
            message.Key,
            message.Audience,
            message.Audience == MemberMessageAudience.AllMembers ? null : memberKeys.Count,
            results));
    }

    private async Task<MemberMessageSenderResult> SendWithAsync(IMemberMessageSender sender, MemberMessage message, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SenderTimeout);

        try
        {
            MemberMessageResult result = await sender.SendAsync(message, timeout.Token).WaitAsync(timeout.Token);
            return new MemberMessageSenderResult(sender.Alias, sender.Name, result.Delivered, result.Note, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MemberMessageSenderResult(sender.Alias, sender.Name, null, null, "Timed out");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Member message sender {Alias} failed", sender.Alias);
            return new MemberMessageSenderResult(sender.Alias, sender.Name, null, null, "Failed");
        }
    }
}
