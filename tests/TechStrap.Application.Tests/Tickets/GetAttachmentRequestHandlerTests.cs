using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class GetAttachmentRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IAttachmentStore _store = Substitute.For<IAttachmentStore>();
    private readonly CapturingLogger _logger = new();

    public GetAttachmentRequestHandlerTests() =>
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));

    private GetAttachmentRequestHandler Handler() => new(_claims, _tickets, _store, _logger);

    private Attachment GivenAttachment()
    {
        var attachment = Attachment.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "pixel.png", "image/png", 7, "key-1", _clock).Value;
        _tickets.GetAttachmentByIdAsync(attachment.Id, Arg.Any<CancellationToken>()).Returns(attachment);
        return attachment;
    }

    [Fact]
    public async Task An_agent_gets_the_stream_name_type_and_size()
    {
        var attachment = GivenAttachment();
        using var stream = new MemoryStream([1, 2, 3]);
        _store.OpenReadAsync("key-1", Arg.Any<CancellationToken>()).Returns(stream);

        var result = await Handler().HandleAsync(attachment.Id, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldSatisfyAllConditions(
            c => c.Content.ShouldBeSameAs(stream),
            c => c.FileName.ShouldBe("pixel.png"),
            c => c.ContentType.ShouldBe("image/png"),
            c => c.Size.ShouldBe(7));
    }

    [Fact]
    public async Task An_unknown_attachment_is_404()
    {
        var result = await Handler().HandleAsync(Guid.CreateVersion7(), Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-not-found");
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.NotFound);
        await _store.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
    }

    [Fact]
    public async Task A_missing_stored_file_is_404_and_logged()
    {
        var attachment = GivenAttachment();
        _store.OpenReadAsync("key-1", Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await Handler().HandleAsync(attachment.Id, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-not-found");
        _logger.Messages.ShouldHaveSingleItem().ShouldContain($"Attachment {attachment.Id} has no stored file.");
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_without_a_lookup()
    {
        _claims.Current.Returns((AgentClaims?)null);

        var result = await Handler().HandleAsync(Guid.CreateVersion7(), Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.AccessRequired().Code);
        await _tickets.DidNotReceiveWithAnyArgs().GetAttachmentByIdAsync(default, Ct);
    }

    private sealed class CapturingLogger : ILogger<GetAttachmentRequestHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add($"{logLevel}: {formatter(state, exception)}");
    }
}
