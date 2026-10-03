using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Email;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Email;

public sealed class DrainEmailOutboxHandlerTests
{
    private static readonly TicketConfirmationEmail Model = new("ORB-1", "Cannot sign in", "Ann", "https://help.example/t/abc", null);
    private static readonly RenderedEmail Rendered = new("subj", "text", "<p>html</p>", "Orbitly <help@orbitly.test>", "reply@orbitly.test");

    private readonly FakeTimeProvider _clock = new();
    private readonly IEmailOutboxStore _store = Substitute.For<IEmailOutboxStore>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IEmailTemplateRenderer _renderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly IOutboundEmailSender _sender = Substitute.For<IOutboundEmailSender>();
    private readonly Product _product;
    private readonly DrainEmailOutboxHandler _handler;

    public DrainEmailOutboxHandlerTests()
    {
        var branding = ProductBranding.Create("Orbitly", "https://cdn.orbitly.test/l.png", "#7C3AED", "help@orbitly.test", "reply@orbitly.test").Value;
        _product = Product.Create("orbitly", "Orbitly", "ORB", branding, _clock).Value;
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
        _store.MarkSentAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _store.MarkFailedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _renderer.RenderTicketConfirmation(Arg.Any<TicketConfirmationEmail>(), Arg.Any<EmailBranding>()).Returns(Rendered);
        _sender.SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _handler = new DrainEmailOutboxHandler(_store, _products, _renderer, _sender, Options.Create(new EmailOutboxWorkerOptions()), NullLogger<DrainEmailOutboxHandler>.Instance);
    }

    private EmailOutboxItem Item(string kind = EmailTemplates.TicketConfirmation, string? payload = null, Guid? productId = null) =>
        EmailOutboxItem.Enqueue(kind, "ann@example.com", payload ?? JsonSerializer.Serialize(Model, JsonSerializerOptions.Web), productId ?? _product.Id, Guid.NewGuid(), _clock).Value;

    private void Claims(params EmailOutboxItem[] items) =>
        _store.ClaimBatchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(items);

    [Fact]
    public async Task It_claims_with_the_configured_batch_size_and_lease()
    {
        Claims();
        using var source = new CancellationTokenSource();

        await _handler.HandleAsync("w1", source.Token);

        await _store.Received(1).ClaimBatchAsync("w1", 20, TimeSpan.FromSeconds(120), source.Token);
    }

    [Fact]
    public async Task A_confirmation_is_rendered_with_the_products_branding_and_sent_with_the_outbox_id_as_message_id()
    {
        var item = Item();
        Claims(item);
        using var source = new CancellationTokenSource();

        var result = await _handler.HandleAsync("w1", source.Token);

        _renderer.Received(1).RenderTicketConfirmation(
            Arg.Is<TicketConfirmationEmail>(m => m == Model),
            Arg.Is<EmailBranding>(b => b == new EmailBranding("Orbitly", "https://cdn.orbitly.test/l.png", "#7C3AED", "help@orbitly.test", "reply@orbitly.test")));
        await _sender.Received(1).SendAsync(
            new OutboundEmail("ann@example.com", "subj", "text", "<p>html</p>", "Orbitly <help@orbitly.test>", "reply@orbitly.test", OutboundMessageIds.For(item.Id)),
            source.Token);
        await _store.Received(1).MarkSentAsync(item.Id, "w1", source.Token);
        result.Value.ShouldBe(new DrainResult(1, 1, 0));
    }

    [Fact]
    public async Task A_send_failure_marks_the_row_failed_with_the_sanitised_category()
    {
        var item = Item();
        Claims(item);
        _sender.SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new ResultError(EmailSendFailures.Transient, "x", ResultErrorKind.Failure)));
        using var source = new CancellationTokenSource();

        var result = await _handler.HandleAsync("w1", source.Token);

        await _store.Received(1).MarkFailedAsync(item.Id, "w1", "smtp-transient", source.Token);
        result.Value.ShouldBe(new DrainResult(1, 0, 1));
    }

    [Fact]
    public async Task An_unknown_kind_is_failed_without_sending() =>
        await AssertFailedWithoutSending(Item(kind: "mystery"), DrainFailures.UnknownKind);

    [Fact]
    public async Task An_unreadable_payload_is_failed_without_sending() =>
        await AssertFailedWithoutSending(Item(payload: "{}"), DrainFailures.PayloadInvalid);

    [Fact]
    public async Task A_missing_or_deleted_product_is_failed_without_sending()
    {
        var other = Guid.NewGuid();
        _products.GetByIdAsync(other, Arg.Any<CancellationToken>()).Returns((Product?)null);

        await AssertFailedWithoutSending(Item(productId: other), DrainFailures.ProductMissing);
    }

    [Fact]
    public async Task A_renderer_exception_fails_that_row_and_the_batch_continues()
    {
        var first = Item();
        var second = Item();
        Claims(first, second);
        _renderer.RenderTicketConfirmation(Arg.Any<TicketConfirmationEmail>(), Arg.Any<EmailBranding>())
            .Returns(_ => throw new InvalidOperationException("boom"), _ => Rendered);

        var result = await _handler.HandleAsync("w1", CancellationToken.None);

        await _store.Received(1).MarkFailedAsync(first.Id, "w1", DrainFailures.RenderFailed, Arg.Any<CancellationToken>());
        await _sender.Received(1).SendAsync(Arg.Is<OutboundEmail>(e => e.MessageId == OutboundMessageIds.For(second.Id)), Arg.Any<CancellationToken>());
        result.Value.ShouldBe(new DrainResult(2, 1, 1));
    }

    [Fact]
    public async Task Products_are_loaded_once_per_batch()
    {
        Claims(Item(), Item(), Item());

        await _handler.HandleAsync("w1", CancellationToken.None);

        await _products.Received(1).GetByIdAsync(_product.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_lost_lease_on_mark_sent_is_logged_and_still_counted_as_sent()
    {
        var item = Item();
        Claims(item);
        _store.MarkSentAsync(item.Id, "w1", Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new ResultError("outbox-not-claim-owner", "x", ResultErrorKind.Conflict)));

        var result = await _handler.HandleAsync("w1", CancellationToken.None);

        result.Value.ShouldBe(new DrainResult(1, 1, 0));
    }

    [Fact]
    public async Task An_empty_batch_sends_nothing()
    {
        Claims();

        var result = await _handler.HandleAsync("w1", CancellationToken.None);

        result.Value.ShouldBe(new DrainResult(0, 0, 0));
        await _sender.DidNotReceive().SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_reaches_the_store_and_the_sender()
    {
        var item = Item();
        Claims(item);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _sender.SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException(source.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => _handler.HandleAsync("w1", source.Token));

        await _store.Received(1).ClaimBatchAsync("w1", 20, TimeSpan.FromSeconds(120), source.Token);
        await _sender.Received(1).SendAsync(Arg.Any<OutboundEmail>(), source.Token);
    }

    private async Task AssertFailedWithoutSending(EmailOutboxItem item, string category)
    {
        Claims(item);

        var result = await _handler.HandleAsync("w1", CancellationToken.None);

        await _store.Received(1).MarkFailedAsync(item.Id, "w1", category, Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>());
        result.Value.ShouldBe(new DrainResult(1, 0, 1));
    }
}
