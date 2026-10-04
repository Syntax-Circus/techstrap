using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Attachments;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using SyntaxCircus.Common;

namespace TechStrap.Application.Tests.Tickets.Customer;

public sealed class GetCustomerAttachmentRequestHandlerTests
{
    private const string Raw = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly FakeTimeProvider _clock = new();
    private readonly IAccessTokenService _tokens = Substitute.For<IAccessTokenService>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IRequesterRepository _requesters = Substitute.For<IRequesterRepository>();
    private readonly IAttachmentStore _store = Substitute.For<IAttachmentStore>();
    private readonly ListLogger _logger = new();
    private readonly Ticket _ticket;
    private readonly Attachment _attachment;

    public GetCustomerAttachmentRequestHandlerTests()
    {
        var ann = Requester.Create("ann@example.com", "Ann", null, _clock).Value;
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, _clock).Value;
        _ticket = TicketBuilder.New(_clock, product.Id, ann.Id, 42);
        var token = TicketAccessToken.Issue(_ticket.Id, ann.Id, "sha256:abc", _clock).Value;
        _attachment = Attachment.Restore(Guid.NewGuid(), _ticket.Id, Guid.NewGuid(), "log.txt", "text/plain", 10, "key/1", _clock.GetUtcNow());

        _tokens.Hash(Raw).Returns("sha256:abc");
        _tickets.GetAccessTokenByHashAsync("sha256:abc", Arg.Any<CancellationToken>()).Returns(token);
        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns(_ticket);
        _requesters.GetByIdAsync(ann.Id, Arg.Any<CancellationToken>()).Returns(ann);
        _tickets.GetAttachmentAsync(_ticket.Id, _attachment.Id, true, Arg.Any<CancellationToken>()).Returns(_attachment);
    }

    private GetCustomerAttachmentRequestHandler Handler() => new(_tokens, _tickets, _requesters, _store, _clock, _logger);

    [Fact]
    public async Task A_public_attachment_of_the_tokens_ticket_streams()
    {
        await using var stream = new MemoryStream([1, 2, 3]);
        _store.OpenReadAsync("key/1", Arg.Any<CancellationToken>()).Returns(stream);

        var result = await Handler().HandleAsync(Raw, _attachment.Id, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Content.ShouldBeSameAs(stream);
        result.Value.FileName.ShouldBe("log.txt");
        result.Value.ContentType.ShouldBe("text/plain");
        result.Value.Size.ShouldBe(10);
    }

    [Fact]
    public async Task Another_tickets_or_an_internal_note_attachment_is_the_uniform_not_found()
    {
        // The repository returns null for both: publicOnly hides internal notes and the ticket id scopes the lookup.
        var result = await Handler().HandleAsync(Raw, Guid.NewGuid(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].ShouldBe(CustomerErrors.NotFound());
        await _tickets.Received(1).GetAttachmentAsync(_ticket.Id, Arg.Any<Guid>(), true, Arg.Any<CancellationToken>());
        await _store.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_missing_file_is_the_uniform_not_found_and_logged()
    {
        _store.OpenReadAsync("key/1", Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await Handler().HandleAsync(Raw, _attachment.Id, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].ShouldBe(CustomerErrors.NotFound());
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(_attachment.Id.ToString());
        entry.Message.ShouldNotContain(Raw);
    }

    [Fact]
    public async Task A_bad_token_is_the_uniform_not_found_without_an_attachment_lookup()
    {
        var result = await Handler().HandleAsync("garbage", _attachment.Id, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].ShouldBe(CustomerErrors.NotFound());
        await _tickets.DidNotReceiveWithAnyArgs().GetAttachmentAsync(default, default, default, Ct);
        await _store.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
    }

    private sealed class ListLogger : ILogger<GetCustomerAttachmentRequestHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
