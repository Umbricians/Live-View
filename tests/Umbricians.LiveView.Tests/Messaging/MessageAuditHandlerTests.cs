using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;

namespace Umbricians.LiveView.Tests.Messaging;

public class MessageAuditHandlerTests
{
    private RecordingAuditService _audit;
    private MessageAuditHandler _handler;
    private Guid _sender;

    [SetUp]
    public void SetUp()
    {
        _audit = new RecordingAuditService();
        _sender = Guid.NewGuid();
        _handler = new MessageAuditHandler(_audit, NullLogger<MessageAuditHandler>.Instance);
    }

    [Test]
    public async Task A_backoffice_message_is_audited_as_sent_by_its_sender()
    {
        var message = new BackofficeMessage(Guid.NewGuid(), "Heads up", "", "default", "Me", DateTimeOffset.UnixEpoch);

        await _handler.HandleAsync(
            new BackofficeMessageSentNotification(message, [Guid.NewGuid(), Guid.NewGuid()], _sender), CancellationToken.None);

        Assert.That(_audit.Entries, Has.Exactly(1).Items);
        AuditEntry entry = _audit.Entries.Single();
        Assert.That(entry.Type, Is.EqualTo(AuditType.Notify));
        Assert.That(entry.UserKey, Is.EqualTo(_sender));
        Assert.That(entry.EntityType, Is.EqualTo("Umbricians.LiveView"));
        Assert.That(entry.Comment, Is.EqualTo("Sent a message to 2 backoffice users: Heads up"));
        Assert.That(entry.Parameters, Is.EqualTo(message.Key.ToString()));
    }

    [Test]
    public async Task A_member_message_names_only_the_methods_that_delivered_it()
    {
        var message = new MemberMessage(
            Guid.NewGuid(), "Hello", "", MemberMessageType.Information, MemberMessageAudience.AllMembers, [], "Me",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        MemberMessageSenderResult[] results =
        [
            new("banner", "Banner", 3, null, null),
            new("email", "Email", null, null, "Failed"),
        ];

        await _handler.HandleAsync(new MemberMessageSentNotification(message, results, _sender), CancellationToken.None);

        Assert.That(_audit.Entries, Has.Exactly(1).Items);
        Assert.That(_audit.Entries.Single().Comment, Is.EqualTo("Sent a message to every member by Banner (Email failed): Hello"));
    }

    [Test]
    public async Task A_member_message_every_method_delivered_lists_them_all()
    {
        MemberMessage message = EveryMemberMessage();
        MemberMessageSenderResult[] results = [new("banner", "Banner", 3, null, null), new("email", "Email", null, null, null)];

        await _handler.HandleAsync(new MemberMessageSentNotification(message, results, _sender), CancellationToken.None);

        Assert.That(_audit.Entries, Has.Exactly(1).Items);
        Assert.That(_audit.Entries.Single().Comment, Is.EqualTo("Sent a message to every member by Banner, Email: Hello"));
    }

    [Test]
    public async Task A_member_message_no_method_delivered_is_audited_as_not_delivered()
    {
        MemberMessage message = EveryMemberMessage();
        MemberMessageSenderResult[] results =
        [
            new("banner", "Banner", null, null, "Timed out"),
            new("email", "Email", null, null, "Failed"),
        ];

        await _handler.HandleAsync(new MemberMessageSentNotification(message, results, _sender), CancellationToken.None);

        Assert.That(_audit.Entries, Has.Exactly(1).Items);
        AuditEntry entry = _audit.Entries.Single();
        Assert.That(entry.Comment, Is.EqualTo("Message to every member not delivered (Banner timed out, Email failed): Hello"));
        Assert.That(entry.Type, Is.EqualTo(AuditType.Notify));
        Assert.That(entry.UserKey, Is.EqualTo(_sender));
        Assert.That(entry.Parameters, Is.EqualTo(message.Key.ToString()));
    }

    private static MemberMessage EveryMemberMessage() => new(
        Guid.NewGuid(), "Hello", "", MemberMessageType.Information, MemberMessageAudience.AllMembers, [], "Me",
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Test]
    public async Task A_message_with_no_known_sender_is_not_audited_and_does_not_fail()
    {
        var message = new BackofficeMessage(Guid.NewGuid(), "Heads up", "", "default", null, DateTimeOffset.UnixEpoch);

        await _handler.HandleAsync(new BackofficeMessageSentNotification(message, [Guid.NewGuid()], null), CancellationToken.None);

        Assert.That(_audit.Entries, Is.Empty);
    }

    private sealed record AuditEntry(AuditType Type, Guid UserKey, string? EntityType, string? Comment, string? Parameters);

    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task<Attempt<AuditLogOperationStatus>> AddAsync(
            AuditType type, Guid userKey, int objectId, string? entityType, string? comment = null, string? parameters = null)
        {
            Entries.Add(new AuditEntry(type, userKey, entityType, comment, parameters));
            return Task.FromResult(Attempt.Succeed(AuditLogOperationStatus.Success));
        }

        public void Add(AuditType type, int userId, int objectId, string? entityType, string comment, string? parameters = null) =>
            throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetLogs(int objectId) => throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetUserLogs(int userId, AuditType type, DateTime? sinceDate = null) =>
            throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetLogs(AuditType type, DateTime? sinceDate = null) => throw new NotSupportedException();

        public void CleanLogs(int maximumAgeOfLogsInMinutes) => throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetPagedItemsByEntity(
            int entityId, long pageIndex, int pageSize, out long totalRecords, Direction orderDirection = Direction.Descending,
            AuditType[]? auditTypeFilter = null, IQuery<IAuditItem>? customFilter = null) => throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetPagedItemsByUser(
            int userId, long pageIndex, int pageSize, out long totalRecords, Direction orderDirection = Direction.Descending,
            AuditType[]? auditTypeFilter = null, IQuery<IAuditItem>? customFilter = null) => throw new NotSupportedException();

        public IAuditEntry Write(
            int performingUserId, string perfomingDetails, string performingIp, DateTime eventDateUtc, int affectedUserId,
            string? affectedDetails, string eventType, string eventDetails) => throw new NotSupportedException();
    }
}
