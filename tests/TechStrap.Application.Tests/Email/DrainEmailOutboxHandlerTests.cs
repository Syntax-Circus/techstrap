using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Email;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Email;

public sealed class DrainEmailOutboxHandlerTests
{
    private static readonly TicketConfirmationEmail Model = new("ORB-1", "Cannot sign in", "Ann", "https://help.example/t/abc", null);
    private static readonly RenderedEmail Rendered = new("subj", "text", "<p>html</p>", "Orbitly <help@orbitly.test>", "reply@orbitly.test");

    private readonly FakeTimeProvider _clock = new();
    private readonly IEmailOutboxStore _store = Substitute.For<IEmailOutboxStore>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IEmailTemplateRenderer _renderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IKbRepository _kb = Substitute.For<IKbRepository>();
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
        _handler = new DrainEmailOutboxHandler(_store, _products, _tickets, _kb, _renderer, _sender, Options.Create(new EmailOutboxWorkerOptions()), NullLogger<DrainEmailOutboxHandler>.Instance);
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

        await _store.Received(1).ClaimBatchAsync("w1", 20, TimeSpan.FromSeconds(900), source.Token);
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
        await _store.Received(1).MarkSentAsync(item.Id, "w1", CancellationToken.None);
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

        await _store.Received(1).ClaimBatchAsync("w1", 20, TimeSpan.FromSeconds(900), source.Token);
        await _sender.Received(1).SendAsync(Arg.Any<OutboundEmail>(), source.Token);
        await _store.DidNotReceive().MarkFailedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().MarkSentAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cancellation_during_a_successful_send_still_marks_the_row_sent()
    {
        var item = Item();
        Claims(item);
        using var source = new CancellationTokenSource();
        _sender.SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            source.Cancel();
            return Result.Success();
        });

        var result = await _handler.HandleAsync("w1", source.Token);

        await _store.Received(1).MarkSentAsync(item.Id, "w1", CancellationToken.None);
        result.Value.ShouldBe(new DrainResult(1, 1, 0));
    }

    private static readonly Guid MessageId = Guid.NewGuid();

    private static readonly AgentReplyEmail ReplyModel = new("ORB-1", "Cannot sign in", "Ann", "https://help.example/t/abc", "Sam from Orbitly Support", MessageId, false);

    private Message AgentMessage(MessageVisibility visibility) =>
        Message.Restore(MessageId, Guid.NewGuid(), AuthorType.Agent, Guid.NewGuid(), visibility, "<p>Hello</p>", null, null, _clock.GetUtcNow());

    [Fact]
    public async Task An_agent_reply_row_renders_with_the_message_loaded_at_send_time()
    {
        var item = Item(EmailTemplates.AgentReply, JsonSerializer.Serialize(ReplyModel, JsonSerializerOptions.Web));
        Claims(item);
        _tickets.GetMessageAsync(MessageId, Arg.Any<CancellationToken>()).Returns(AgentMessage(MessageVisibility.Public));
        _renderer.RenderAgentReply(Arg.Any<AgentReplyEmail>(), Arg.Any<string>(), Arg.Any<EmailBranding>()).Returns(Rendered);

        var result = await _handler.HandleAsync("w1", CancellationToken.None);

        _renderer.Received(1).RenderAgentReply(ReplyModel, "<p>Hello</p>", Arg.Any<EmailBranding>());
        await _store.Received(1).MarkSentAsync(item.Id, "w1", CancellationToken.None);
        result.Value.ShouldBe(new DrainResult(1, 1, 0));
    }

    private static readonly Guid ArticleA = Guid.NewGuid();
    private static readonly Guid ArticleB = Guid.NewGuid();
    private const string LinkA = "https://help.example/p/orbitly/kb/account/reset";
    private const string LinkB = "https://help.example/p/orbitly/kb/general/export";

    private async Task<AgentReplyEmail?> DrainReplyWithArticlesAsync(IReadOnlyList<ArticleLinkEntry> articles, params PublicKbLinkTarget[] current)
    {
        var item = Item(EmailTemplates.AgentReply, JsonSerializer.Serialize(ReplyModel with { Articles = articles }, JsonSerializerOptions.Web));
        Claims(item);
        _tickets.GetMessageAsync(MessageId, Arg.Any<CancellationToken>()).Returns(AgentMessage(MessageVisibility.Public));
        _kb.ListPublicLinkTargetsAsync(_product.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(current);
        AgentReplyEmail? rendered = null;
        _renderer.RenderAgentReply(Arg.Do<AgentReplyEmail>(m => rendered = m), Arg.Any<string>(), Arg.Any<EmailBranding>()).Returns(Rendered);

        await _handler.HandleAsync("w1", CancellationToken.None);

        await _kb.Received(1).ListPublicLinkTargetsAsync(_product.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        return rendered;
    }

    [Fact]
    public async Task Review_Focus_5_a_linked_article_that_is_still_published_at_the_same_address_is_kept_and_one_query_serves_the_email()
    {
        var rendered = await DrainReplyWithArticlesAsync(
            [new ArticleLinkEntry("Reset", LinkA, ArticleA), new ArticleLinkEntry("Export", LinkB, ArticleB)],
            new PublicKbLinkTarget(ArticleA, "account", "reset"), new PublicKbLinkTarget(ArticleB, "general", "export"));

        rendered!.Articles!.Select(a => a.Url).ShouldBe([LinkA, LinkB]);
    }

    [Fact]
    public async Task Review_Focus_5_an_article_archived_after_planning_is_dropped_at_send_time()
    {
        var rendered = await DrainReplyWithArticlesAsync(
            [new ArticleLinkEntry("Reset", LinkA, ArticleA), new ArticleLinkEntry("Export", LinkB, ArticleB)],
            new PublicKbLinkTarget(ArticleB, "general", "export"));

        rendered!.Articles!.ShouldHaveSingleItem().Url.ShouldBe(LinkB);
    }

    [Fact]
    public async Task Review_Focus_5_an_article_whose_slug_or_category_changed_is_dropped_and_an_email_with_no_links_left_renders_without_a_list()
    {
        var rendered = await DrainReplyWithArticlesAsync(
            [new ArticleLinkEntry("Reset", LinkA, ArticleA), new ArticleLinkEntry("Export", LinkB, ArticleB)],
            new PublicKbLinkTarget(ArticleA, "account", "reset-v2"), new PublicKbLinkTarget(ArticleB, "other-category", "export"));

        rendered!.Articles.ShouldBeNull();
    }

    [Fact]
    public async Task Review_Focus_5_a_product_deactivated_after_planning_loses_every_link_at_send_time_and_the_knowledge_base_is_not_asked()
    {
        _product.SetActive(false);
        var item = Item(EmailTemplates.AgentReply, JsonSerializer.Serialize(ReplyModel with { Articles = [new ArticleLinkEntry("Reset", LinkA, ArticleA), new ArticleLinkEntry("Old", LinkB)] }, JsonSerializerOptions.Web));
        Claims(item);
        _tickets.GetMessageAsync(MessageId, Arg.Any<CancellationToken>()).Returns(AgentMessage(MessageVisibility.Public));
        AgentReplyEmail? rendered = null;
        _renderer.RenderAgentReply(Arg.Do<AgentReplyEmail>(m => rendered = m), Arg.Any<string>(), Arg.Any<EmailBranding>()).Returns(Rendered);

        await _handler.HandleAsync("w1", CancellationToken.None);

        rendered.ShouldNotBeNull().Articles.ShouldBeNull();
        await _kb.DidNotReceiveWithAnyArgs().ListPublicLinkTargetsAsync(default, default!, TestContext.Current.CancellationToken);
        await _store.Received(1).MarkSentAsync(item.Id, "w1", CancellationToken.None);
    }

    [Fact]
    public async Task An_article_with_an_empty_id_is_dropped_and_never_kept_unchecked()
    {
        var rendered = await DrainReplyWithArticlesAsync(
            [new ArticleLinkEntry("Nobody", LinkA, Guid.Empty), new ArticleLinkEntry("Export", LinkB, ArticleB)],
            new PublicKbLinkTarget(ArticleB, "general", "export"));

        rendered!.Articles!.ShouldHaveSingleItem().Url.ShouldBe(LinkB);
    }

    [Fact]
    public async Task A_row_queued_before_article_ids_existed_is_rendered_as_it_is_without_asking_the_knowledge_base()
    {
        var item = Item(EmailTemplates.AgentReply, JsonSerializer.Serialize(ReplyModel with { Articles = [new ArticleLinkEntry("Old", LinkA)] }, JsonSerializerOptions.Web));
        Claims(item);
        _tickets.GetMessageAsync(MessageId, Arg.Any<CancellationToken>()).Returns(AgentMessage(MessageVisibility.Public));
        _renderer.RenderAgentReply(Arg.Any<AgentReplyEmail>(), Arg.Any<string>(), Arg.Any<EmailBranding>()).Returns(Rendered);

        await _handler.HandleAsync("w1", CancellationToken.None);

        _renderer.Received(1).RenderAgentReply(Arg.Is<AgentReplyEmail>(m => m.Articles!.Single().Url == LinkA), Arg.Any<string>(), Arg.Any<EmailBranding>());
        await _kb.DidNotReceiveWithAnyArgs().ListPublicLinkTargetsAsync(default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_agent_reply_whose_message_is_gone_or_internal_fails_as_message_missing()
    {
        var payload = JsonSerializer.Serialize(ReplyModel, JsonSerializerOptions.Web);
        _tickets.GetMessageAsync(MessageId, Arg.Any<CancellationToken>()).Returns((Message?)null);
        await AssertFailedWithoutSending(Item(EmailTemplates.AgentReply, payload), DrainFailures.MessageMissing);

        _tickets.GetMessageAsync(MessageId, Arg.Any<CancellationToken>()).Returns(AgentMessage(MessageVisibility.Internal));
        await AssertFailedWithoutSending(Item(EmailTemplates.AgentReply, payload), DrainFailures.MessageMissing);
    }

    [Theory]
    [InlineData("ticket-solved")]
    [InlineData("ticket-assigned")]
    public async Task Solved_and_assignment_rows_render_and_send(string kind)
    {
        var payload = kind == EmailTemplates.TicketSolved
            ? JsonSerializer.Serialize(new TicketSolvedEmail("ORB-1", "S", "Ann", "https://help.example/t/abc", 7), JsonSerializerOptions.Web)
            : JsonSerializer.Serialize(new TicketAssignedEmail("ORB-1", "S", "Orbitly", "Mia", null), JsonSerializerOptions.Web);
        var item = Item(kind, payload);
        Claims(item);
        _renderer.RenderTicketSolved(Arg.Any<TicketSolvedEmail>(), Arg.Any<EmailBranding>()).Returns(Rendered);
        _renderer.RenderTicketAssigned(Arg.Any<TicketAssignedEmail>(), Arg.Any<EmailBranding>()).Returns(Rendered);

        var result = await _handler.HandleAsync("w1", CancellationToken.None);

        await _sender.Received(1).SendAsync(Arg.Is<OutboundEmail>(e => e.MessageId == OutboundMessageIds.For(item.Id)), Arg.Any<CancellationToken>());
        await _store.Received(1).MarkSentAsync(item.Id, "w1", CancellationToken.None);
        result.Value.ShouldBe(new DrainResult(1, 1, 0));
    }

    [Fact]
    public async Task A_pre_06b_agent_reply_payload_without_reopen_days_deserialises_to_zero_and_is_rendered()
    {
        var payload = $$"""{"ticketNumber":"ORB-1","subject":"S","requesterName":"Ann","portalLink":"https://help.example/t/abc","agentPublicName":"Sam","messageId":"{{MessageId}}","solved":true}""";
        var item = Item(EmailTemplates.AgentReply, payload);
        Claims(item);
        _tickets.GetMessageAsync(MessageId, Arg.Any<CancellationToken>()).Returns(AgentMessage(MessageVisibility.Public));
        _renderer.RenderAgentReply(Arg.Any<AgentReplyEmail>(), Arg.Any<string>(), Arg.Any<EmailBranding>()).Returns(Rendered);

        var result = await _handler.HandleAsync("w1", CancellationToken.None);

        // Zero is the marker the renderer turns into TicketNotices.DefaultReopenDays (see the renderer tests).
        _renderer.Received(1).RenderAgentReply(Arg.Is<AgentReplyEmail>(m => m.ReopenDays == 0 && m.Solved), Arg.Any<string>(), Arg.Any<EmailBranding>());
        result.Value.ShouldBe(new DrainResult(1, 1, 0));
    }

    [Theory]
    [InlineData("new-ticket-alert")]
    [InlineData("customer-reply-alert")]
    [InlineData("access-links")]
    public async Task New_kinds_render_and_send(string kind)
    {
        var payload = kind switch
        {
            EmailTemplates.NewTicketAlert => JsonSerializer.Serialize(new NewTicketAlertEmail("ORB-1", "S", "Orbitly", "Ann", false, null), JsonSerializerOptions.Web),
            EmailTemplates.CustomerReplyAlert => JsonSerializer.Serialize(new CustomerReplyAlertEmail("ORB-1", "S", "Orbitly", true, null), JsonSerializerOptions.Web),
            _ => JsonSerializer.Serialize(new AccessLinksEmail("Ann", [new AccessLinkEntry("ORB-1", "S", "https://help.example/t/abc")]), JsonSerializerOptions.Web),
        };
        var item = Item(kind, payload);
        Claims(item);
        _renderer.RenderNewTicketAlert(Arg.Any<NewTicketAlertEmail>(), Arg.Any<EmailBranding>()).Returns(Rendered);
        _renderer.RenderCustomerReplyAlert(Arg.Any<CustomerReplyAlertEmail>(), Arg.Any<EmailBranding>()).Returns(Rendered);
        _renderer.RenderAccessLinks(Arg.Any<AccessLinksEmail>(), Arg.Any<EmailBranding>()).Returns(Rendered);

        var result = await _handler.HandleAsync("w1", CancellationToken.None);

        await _sender.Received(1).SendAsync(Arg.Is<OutboundEmail>(e => e.MessageId == OutboundMessageIds.For(item.Id)), Arg.Any<CancellationToken>());
        await _store.Received(1).MarkSentAsync(item.Id, "w1", CancellationToken.None);
        result.Value.ShouldBe(new DrainResult(1, 1, 0));
    }

    [Theory]
    [InlineData("""{"ticketNumber":"ORB-1","subject":"S","productName":"","requesterLabel":"Ann"}""", "new-ticket-alert")]
    [InlineData("""{"ticketNumber":"ORB-1","subject":"S","productName":"Orbitly","requesterLabel":""}""", "new-ticket-alert")]
    [InlineData("""{"ticketNumber":"ORB-1","subject":"S","productName":""}""", "customer-reply-alert")]
    [InlineData("""{"requesterName":"Ann","links":[]}""", "access-links")]
    [InlineData("""{"requesterName":"Ann"}""", "access-links")]
    [InlineData("""{"links":[{"ticketNumber":"","subject":"S","portalLink":"https://x.test"}]}""", "access-links")]
    [InlineData("""{"links":[{"ticketNumber":"ORB-1","subject":"S","portalLink":""}]}""", "access-links")]
    public async Task A_new_kind_with_a_missing_required_field_fails_as_payload_invalid(string payload, string kind) =>
        await AssertFailedWithoutSending(Item(kind, payload), DrainFailures.PayloadInvalid);

    [Theory]
    [InlineData("ticket-confirmation")]
    [InlineData("agent-reply")]
    [InlineData("ticket-solved")]
    [InlineData("new-ticket-alert")]
    [InlineData("customer-reply-alert")]
    [InlineData("access-links")]
    [InlineData("ticket-assigned")]
    public async Task Each_kind_with_a_payload_missing_required_fields_fails_as_payload_invalid(string kind) =>
        await AssertFailedWithoutSending(Item(kind, "{}"), DrainFailures.PayloadInvalid);

    [Fact]
    public async Task An_agent_reply_with_a_blank_public_name_fails_as_payload_invalid() =>
        await AssertFailedWithoutSending(
            Item(EmailTemplates.AgentReply, JsonSerializer.Serialize(ReplyModel with { AgentPublicName = " " }, JsonSerializerOptions.Web)),
            DrainFailures.PayloadInvalid);

    private async Task AssertFailedWithoutSending(EmailOutboxItem item, string category)
    {
        Claims(item);

        var result = await _handler.HandleAsync("w1", CancellationToken.None);

        await _store.Received(1).MarkFailedAsync(item.Id, "w1", category, Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>());
        result.Value.ShouldBe(new DrainResult(1, 0, 1));
    }
}
