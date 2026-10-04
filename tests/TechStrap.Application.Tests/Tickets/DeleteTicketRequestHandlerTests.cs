using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class DeleteTicketRequestHandlerTests
{
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IAttachmentStore _attachments = Substitute.For<IAttachmentStore>();
    private readonly IEmailOutboxStore _outbox = Substitute.For<IEmailOutboxStore>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _admin;
    private readonly Ticket _ticket;

    public DeleteTicketRequestHandlerTests()
    {
        _admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(_admin);
        _ticket = TicketBuilder.New(_clock);
        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns(_ticket);
        _tickets.GetAttachmentsAsync(_ticket.Id, false, Arg.Any<CancellationToken>()).Returns(
            [Stored("attachments/a/1"), Stored("attachments/a/2")]);
        _tickets.GetMessagesAsync(_ticket.Id, false, Arg.Any<CancellationToken>()).Returns(
            [Message(), Message(), Message()]);
    }

    private Attachment Stored(string key) =>
        Attachment.Restore(Guid.NewGuid(), _ticket.Id, Guid.NewGuid(), "a.png", "image/png", 10, key, _clock.GetUtcNow());

    private Message Message()
    {
        var ticket = TicketBuilder.New(_clock);
        return ticket.AddCustomerReply(Guid.NewGuid(), "<p>hi</p>", _clock).Value;
    }

    private sealed class RecordingLogger : ILogger<DeleteTicketRequestHandler>
    {
        public List<(LogLevel Level, Dictionary<string, object?> Properties)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToDictionary(p => p.Key, p => p.Value) : []));
    }

    private DeleteTicketRequestHandler Handler(IUnitOfWork? unitOfWork = null, ILogger<DeleteTicketRequestHandler>? logger = null) =>
        new(_tickets, _attachments, _outbox, _events, _agents, _claims, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock,
            logger ?? NullLogger<DeleteTicketRequestHandler>.Instance);

    [Fact]
    public async Task A_ticket_is_removed_with_its_outbox_rows_and_audited_with_counts_only()
    {
        var result = await Handler().HandleAsync(_ticket.Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _tickets.Received(1).Remove(_ticket);
        await _outbox.Received(1).DeleteForTicketAsync(_ticket.Id, Arg.Any<CancellationToken>());
        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.TicketDeleted && e.SubjectType == AdminSubjectType.Ticket && e.SubjectId == _ticket.Id
            && e.PayloadJson == "{\"number\":\"TS-42\",\"messageCount\":3,\"attachmentCount\":2}"));
    }

    [Fact]
    public async Task The_files_are_deleted_only_after_a_successful_commit()
    {
        var conflict = await Handler(UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict")))
            .HandleAsync(_ticket.Id, TestContext.Current.CancellationToken);

        conflict.Errors.ShouldHaveSingleItem().Code.ShouldBe("concurrency-conflict");
        await _attachments.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        (await Handler().HandleAsync(_ticket.Id, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        await _attachments.Received(1).DeleteAsync("attachments/a/1", CancellationToken.None);
        await _attachments.Received(1).DeleteAsync("attachments/a/2", CancellationToken.None);
    }

    [Fact]
    public async Task A_failing_file_delete_is_logged_and_does_not_fail_the_delete()
    {
        _attachments.DeleteAsync("attachments/a/1", Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("disk"));

        var logger = new RecordingLogger();

        var result = await Handler(logger: logger).HandleAsync(_ticket.Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _attachments.Received(1).DeleteAsync("attachments/a/2", Arg.Any<CancellationToken>());
        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Properties["StorageKey"].ShouldBe("attachments/a/1");
        warning.Properties["ExceptionType"].ShouldBe(nameof(IOException));
    }

    [Fact]
    public async Task An_unknown_ticket_is_not_found_and_nothing_is_staged()
    {
        var result = await Handler().HandleAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("ticket-not-found"),
            error => error.Kind.ShouldBe(ResultErrorKind.NotFound));
        _tickets.DidNotReceive().Remove(Arg.Any<Ticket>());
        await _outbox.DidNotReceive().DeleteForTicketAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task A_deactivated_actor_is_refused_before_anything_is_read()
    {
        _admin.SetActive(false);

        var result = await Handler().HandleAsync(_ticket.Id, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.Codes.Inactive);
        _ = _tickets.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _tickets.DidNotReceive().Remove(Arg.Any<Ticket>());
    }

    [Fact]
    public async Task A_missing_agent_identity_is_refused()
    {
        _claims.Current.Returns((AgentClaims?)null);

        var result = await Handler().HandleAsync(_ticket.Id, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.Codes.AccessRequired);
    }
}
