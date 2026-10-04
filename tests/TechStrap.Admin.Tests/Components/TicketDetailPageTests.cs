using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TicketDetailPageTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly NavigationManager _navigation;

    public TicketDetailPageTests()
    {
        var products = Substitute.For<IProductsClient>();
        var agents = Substitute.For<IAgentsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(agents);
        Services.AddSingleton(tags);
        Services.AddTicketFeatures();
        Services.AddSingleton(AgentSessions.SignedIn());
        ShowTicket(TestData.Detail());
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private void ShowTicket(TicketDetailDto detail) =>
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));

    private IRenderedComponent<TicketDetailPage> RenderTicket() => Render<TicketDetailPage>(p => p.Add(c => c.Number, "ORB-42"));

    [Fact]
    public void The_header_shows_a_copyable_number_subject_status_product_and_caller()
    {
        var cut = RenderTicket();

        cut.Find("code.ts-ticket-number").TextContent.ShouldBe("ORB-42");
        cut.Find("h1.ts-ticket-subject").TextContent.ShouldBe("Cannot log in");
        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Open");
        cut.Find(".ts-ticket-badges .ts-stamp").ClassList.ShouldContain("ts-stamp--ticket");
        cut.Find(".ts-ticket-badges .ts-product").TextContent.ShouldBe("Orbitly");
        cut.FindAll(".ts-callform-field dd").Select(d => d.TextContent.Trim()).ShouldContain("Ada Lovelace");
    }

    [Fact]
    public void While_loading_the_page_shows_a_skeleton_and_then_the_ticket()
    {
        var gate = new TaskCompletionSource<Result<TicketDetailDto>>();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(gate.Task);

        var cut = RenderTicket();

        cut.FindAll(".ts-skeleton").Count.ShouldBe(6);
        cut.FindAll("article.ts-ticket").ShouldBeEmpty();

        gate.SetResult(TestData.Ok(TestData.Detail()));

        cut.WaitForAssertion(() => cut.Find("article.ts-ticket").ShouldNotBeNull());
        cut.FindAll(".ts-skeleton").ShouldBeEmpty();
    }

    [Fact]
    public void A_404_shows_the_plain_not_found_view_with_a_way_back_and_no_ticket_chrome()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "No such ticket.", ResultErrorKind.NotFound));

        var cut = RenderTicket();

        cut.Find("section.ts-gone h1").TextContent.ShouldBe("Ticket not found");
        cut.Find("section.ts-gone a").GetAttribute("href").ShouldBe("/queue");
        cut.FindAll("article.ts-ticket").ShouldBeEmpty();
        cut.FindAll(".ts-window").ShouldBeEmpty();
    }

    [Fact]
    public void Another_failure_shows_an_alert_with_retry_and_retry_loads_the_ticket()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<TicketDetailDto>("boom", "The API is unavailable."),
            TestData.Ok(TestData.Detail()));

        var cut = RenderTicket();

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't load this ticket. The API is unavailable.");

        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => cut.Find("article.ts-ticket").ShouldNotBeNull());
    }

    [Fact]
    public void The_timeline_is_one_ordered_list_of_messages_and_changes()
    {
        var t = TestData.Now.AddHours(-5);
        ShowTicket(TestData.Detail(
            messages: [TestData.Message(at: t, bodyHtml: "<p>Help</p>"), TestData.Message(MessageAuthorTypes.Agent, at: t.AddHours(1), authorName: "Sam Ortiz", bodyHtml: "<p>Try this</p>")],
            events: [TestData.Event(TicketEventTypes.Created, """{"channel":"Email"}""", t, "Requester", "Ada Lovelace"), TestData.Event(TicketEventTypes.StatusChanged, """{"from":"New","to":"Pending"}""", t.AddHours(1))]));

        var cut = RenderTicket();

        var items = cut.FindAll("ol.ts-timeline > li");
        items.Select(i => i.ClassList.Contains("ts-timeline-message") ? "message" : i.QuerySelector(".ts-event-text")!.TextContent).ShouldBe(
            ["Ticket opened via Email", "message", "message", "Status changed from New to Pending"]);
        cut.Find("ol.ts-timeline").GetAttribute("aria-label").ShouldBe("Timeline");
        cut.FindAll(".ts-legend").Count.ShouldBe(1);
    }

    [Fact]
    public void Internal_notes_customer_messages_and_public_replies_are_visibly_distinct_with_text_labels()
    {
        ShowTicket(TestData.Detail(messages:
        [
            TestData.Message(MessageAuthorTypes.Requester),
            TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Public, authorName: "Sam Ortiz"),
            TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Internal, authorName: "Sam Ortiz"),
        ]));

        var cut = RenderTicket();

        cut.FindAll("article.ts-entry--customer").Count.ShouldBe(1);
        cut.FindAll("article.ts-entry--public").Count.ShouldBe(1);
        var note = cut.Find("article.ts-entry--note");
        note.TextContent.ShouldContain("INTERNAL NOTE");
        cut.FindAll(".ts-entry--customer .ts-entry-role").Single().TextContent.ShouldBe("customer");
    }

    [Fact]
    public void The_message_body_is_the_servers_sanitised_html_and_everything_else_is_encoded()
    {
        ShowTicket(TestData.Detail(
            subject: "<img src=x onerror=alert(1)>",
            messages: [TestData.Message(bodyHtml: "<p>Hello <strong>there</strong></p>", authorName: "<b>Mallory</b>")]));

        var cut = RenderTicket();

        cut.Find(".ts-message-body strong").TextContent.ShouldBe("there");
        cut.Find("h1.ts-ticket-subject").InnerHtml.ShouldBe("&lt;img src=x onerror=alert(1)&gt;");
        cut.Find(".ts-entry-head strong").TextContent.ShouldBe("<b>Mallory</b>");
        cut.FindAll("img[onerror]").ShouldBeEmpty();
    }

    [Fact]
    public void Attachments_link_to_the_admin_pass_through_as_downloads()
    {
        var id = Guid.NewGuid();
        ShowTicket(TestData.Detail(messages: [TestData.Message(attachments: [new AttachmentDto(id, "screenshot.png", "image/png", 2048)])]));

        var cut = RenderTicket();

        var link = cut.Find("ul.ts-attachments a");
        link.GetAttribute("href").ShouldBe($"/attachments/{id}");
        link.HasAttribute("download").ShouldBeTrue();
        link.GetAttribute("data-enhance-nav").ShouldBe("false");
        link.TextContent.ShouldBe("screenshot.png");
        cut.Find(".ts-attachment-meta").TextContent.ShouldBe("(image/png, 2 KB)");
    }

    [Fact]
    public void Linked_articles_are_listed_by_title_without_inventing_a_link()
    {
        ShowTicket(TestData.Detail(messages: [TestData.Message(MessageAuthorTypes.Agent, articles: [new LinkedArticleDto(Guid.NewGuid(), "Reset your password", "reset-password")])]));

        var cut = RenderTicket();

        cut.Find("p.ts-articles").TextContent.ShouldBe("Linked articles: Reset your password");
        cut.FindAll("p.ts-articles a").ShouldBeEmpty();
    }

    [Fact]
    public void A_closed_ticket_is_read_only_and_says_why()
    {
        ShowTicket(TestData.Detail(status: TicketStatuses.Closed));

        var cut = RenderTicket();

        cut.Find("p.ts-closed-note").TextContent.ShouldBe("Closed tickets are read-only; a customer reply starts a follow-up");
        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Closed");
        cut.FindAll("textarea, select, form").ShouldBeEmpty();
        cut.FindAll("section.ts-facts").Count.ShouldBe(1);
    }

    [Fact]
    public void An_open_ticket_has_no_closed_note()
    {
        RenderTicket().FindAll("p.ts-closed-note").ShouldBeEmpty();
    }

    [Fact]
    public void A_follow_up_links_to_its_parent_by_number()
    {
        var parentId = Guid.NewGuid();
        ShowTicket(TestData.Detail(parentId: parentId));
        _tickets.GetAsync(parentId.ToString(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail("ORB-7")));

        var cut = RenderTicket();

        var link = cut.Find("p.ts-followup a");
        link.TextContent.ShouldBe("ORB-7");
        link.GetAttribute("href").ShouldBe("/tickets/ORB-7");
        cut.Find("p.ts-followup").TextContent.ShouldStartWith("Follow-up to");
    }

    [Fact]
    public void Metadata_from_a_public_key_is_labelled_untrusted_and_a_trusted_one_is_not()
    {
        ShowTicket(TestData.Detail(metadataJson: """{"appVersion":"2.3.1"}""", metadataTrusted: false));
        var untrusted = RenderTicket();

        untrusted.Find("section.ts-metadata--untrusted .ts-metadata-trust strong").TextContent.ShouldBe("Untrusted");
        untrusted.Find("section.ts-metadata dt").TextContent.ShouldBe("appVersion");
        untrusted.Find("section.ts-metadata dd").TextContent.ShouldBe("2.3.1");

        ShowTicket(TestData.Detail(metadataJson: """{"appVersion":"2.3.1"}""", metadataTrusted: true));
        var trusted = RenderTicket();

        trusted.FindAll(".ts-metadata--untrusted").ShouldBeEmpty();
        trusted.Find("section.ts-metadata .ts-metadata-trust strong").TextContent.ShouldBe("Trusted");
    }

    [Fact]
    public void A_ticket_without_metadata_shows_no_metadata_section()
    {
        RenderTicket().FindAll("section.ts-metadata").ShouldBeEmpty();
    }

    [Fact]
    public void A_closed_tickets_side_panel_shows_the_requester_and_the_facts_read_only_with_no_controls()
    {
        ShowTicket(TestData.Detail(status: TicketStatuses.Closed, priority: TicketPriorities.Urgent, assigneeId: TestData.SamAgentId, assigneeName: "Sam Ortiz",
            tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]));

        var cut = RenderTicket();

        cut.Find("section.ts-requester .ts-requester-email").TextContent.ShouldBe("ada@example.com");
        var facts = cut.Find("section.ts-facts");
        facts.TextContent.ShouldContain("Sam Ortiz");
        facts.QuerySelector(".ts-priority")!.TextContent.ShouldBe("Urgent");
        facts.QuerySelector(".ts-tag")!.TextContent.ShouldBe("bug");
        cut.FindAll("section.ts-sidebar, aside select, aside button").ShouldBeEmpty();
    }

    [Fact]
    public async Task Escape_goes_back_to_the_queue_but_not_while_typing()
    {
        RenderTicket();

        await PressAsync("Escape", typing: true);
        _navigation.Uri.ShouldNotEndWith("/queue");

        await PressAsync("Escape");

        _navigation.Uri.ShouldEndWith("/queue");
    }

    [Fact]
    public async Task A_refresh_keeps_the_ticket_on_screen_and_a_deleted_ticket_becomes_no_longer_exists()
    {
        var cut = RenderTicket();
        var gate = new TaskCompletionSource<Result<TicketDetailDto>>();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(gate.Task);

        var refresh = cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.Find("article.ts-ticket").ShouldNotBeNull();
        cut.FindAll(".ts-skeleton").ShouldBeEmpty();

        gate.SetResult(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));
        await refresh;

        cut.Find("section.ts-gone h1").TextContent.ShouldBe("This ticket no longer exists");
        cut.FindAll("article.ts-ticket").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_ticket_and_shows_an_inline_alert()
    {
        var cut = RenderTicket();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("boom", "The API is unavailable."));

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.Find("article.ts-ticket [role=alert] p").TextContent.ShouldContain("The API is unavailable.");
        cut.Find("code.ts-ticket-number").TextContent.ShouldBe("ORB-42");
    }

    [Fact]
    public void Applying_a_write_response_replaces_the_row_version_and_status_on_screen()
    {
        var cut = RenderTicket();

        cut.InvokeAsync(() => cut.Instance.ApplyState(TestData.State(TicketStatuses.Solved, rowVersion: 12)));

        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Solved");
    }

    [Fact]
    public void The_ticket_number_in_the_route_is_what_the_API_is_asked_for_and_the_token_is_passed()
    {
        RenderTicket();

        _tickets.Received(1).GetAsync("ORB-42", Arg.Is<CancellationToken>(t => t.CanBeCanceled));
    }

    [Fact]
    public void An_open_ticket_gets_the_composer_beneath_the_timeline_and_a_closed_one_does_not()
    {
        var open = RenderTicket();

        open.Find("section.ts-conversation ol.ts-timeline").ShouldNotBeNull();
        open.Find("section.ts-conversation section.ts-composer").ShouldNotBeNull();
        open.Find("section.ts-composer p.ts-composer-audience").TextContent.ShouldBe("To: ada@example.com");

        ShowTicket(TestData.Detail(status: TicketStatuses.Closed));

        RenderTicket().FindAll("section.ts-composer").ShouldBeEmpty();
    }

    [Fact]
    public void A_sent_reply_takes_the_new_row_version_from_the_response_and_reloads_the_timeline()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(TicketStatuses.Pending, rowVersion: 8))));
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Internal), TestData.State(TicketStatuses.Pending, rowVersion: 9))));
        var cut = RenderTicket();
        cut.Find("textarea").Input("On it.");
        ShowTicket(TestData.Detail(status: TicketStatuses.Pending, rowVersion: 8, messages:
            [TestData.Message(), TestData.Message(MessageAuthorTypes.Agent, authorName: "Sam Ortiz", bodyHtml: "<p>On it.</p>")]));

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.WaitForAssertion(() => cut.FindAll("ol.ts-timeline > li.ts-timeline-message").Count.ShouldBe(2));
        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Pending");

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Follow-up note.");
        cut.Find(".ts-composer-actions button").Click();

        _tickets.Received(1).AddNoteAsync(TestData.TicketId, Arg.Is<AddInternalNoteRequest>(r => r.RowVersion == 8u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_draft_in_the_other_tab_survives_the_reload_that_follows_a_send()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(rowVersion: 8))));
        var cut = RenderTicket();
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Half-written note");
        cut.FindAll(".ts-composer-modes button")[0].Click();
        cut.Find("textarea").Input("Reply");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.WaitForAssertion(() => _tickets.Received(2).GetAsync("ORB-42", Arg.Any<CancellationToken>()));
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").GetAttribute("value").ShouldBe("Half-written note");
    }

    [Fact]
    public void A_reply_to_a_ticket_deleted_meanwhile_ends_on_this_ticket_no_longer_exists()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));
        var cut = RenderTicket();
        cut.Find("textarea").Input("Too late.");
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.WaitForAssertion(() => cut.Find("section.ts-gone h1").TextContent.ShouldBe("This ticket no longer exists"));
    }

    [Fact]
    public void A_ticket_that_loaded_cleanly_shows_no_alert_and_no_retry()
    {
        var cut = RenderTicket();

        cut.FindAll("[role=alert]").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Retry");
    }
}
