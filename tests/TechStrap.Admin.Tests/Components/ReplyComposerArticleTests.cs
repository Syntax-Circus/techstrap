using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Linking knowledge base articles from a reply (PHASE-08 T20). The picker is behind a button, so a composer that never links an article never needs the knowledge base client. A choice lives in the ticket's
/// draft: it survives a mode switch, a failed send and a closed screen, a public reply sends exactly those ids in the order they were chosen, a note sends none, and an accepted send takes out the ones it sent.
/// Review Focus 5 (the Admin half): the API's refusal of a link keeps the text and the choice and says what to do.
/// </summary>
public sealed class ReplyComposerArticleTests : AdminComponentTest
{
    private static readonly Guid FirstArticle = Guid.Parse("dddddddd-0000-0000-0000-0000000000b1");
    private static readonly Guid SecondArticle = Guid.Parse("dddddddd-0000-0000-0000-0000000000b2");

    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IKbClient _kb = Substitute.For<IKbClient>();

    public ReplyComposerArticleTests()
    {
        Services.AddSingleton(_tickets);
        Services.AddSingleton(_kb);
        Services.AddScoped<DraftStore>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Accepted(MessageVisibilities.Public));
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Accepted(MessageVisibilities.Internal));
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.KbPage(
        [
            TestData.KbItem("Reset your password", "reset-password", KbArticleStatuses.Published, TestData.OrbitlyId, id: FirstArticle),
            TestData.KbItem("Welcome", "welcome", KbArticleStatuses.Published, productId: null, id: SecondArticle),
        ])));
    }

    private static Result<AgentMessageResponse> Accepted(string visibility) =>
        TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, visibility), TestData.State(rowVersion: 8)));

    private DraftStore Drafts => Services.GetRequiredService<DraftStore>();

    private IRenderedComponent<ReplyComposer> RenderComposer(Guid? productId = null) =>
        Render<ReplyComposer>(p => p
            .Add(c => c.TicketId, TestData.TicketId)
            .Add(c => c.TicketNumber, "ORB-42")
            .Add(c => c.RequesterEmail, "ada@example.com")
            .Add(c => c.ProductId, productId ?? TestData.OrbitlyId)
            .Add(c => c.RowVersion, 7u));

    private IEnumerable<AddAgentReplyRequest> ReplyRequests() =>
        _tickets.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync)).Select(c => (AddAgentReplyRequest)c.GetArguments()[1]!);

    private IEnumerable<ListKbArticlesRequest> Searches() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.ListAsync)).Select(c => (ListKbArticlesRequest)c.GetArguments()[0]!);

    private static void OpenPicker(IRenderedComponent<ReplyComposer> cut) => cut.Find("button.ts-composer-link-article").Click();

    private void SearchAndAdd(IRenderedComponent<ReplyComposer> cut, params string[] titles)
    {
        cut.Find(".ts-kb-picker input[type=search]").Input("a");
        Time.Advance(KbDefaults.PickerDebounce);
        cut.WaitForAssertion(() => cut.FindAll(".ts-kb-picker-results li").Count.ShouldBeGreaterThan(0));
        foreach (var title in titles)
        {
            cut.FindAll(".ts-kb-picker-results li").Single(l => l.QuerySelector(".ts-kb-picker-title")!.TextContent == title).QuerySelector("button")!.Click();
        }
    }

    private static IReadOnlyList<string> Chips(IRenderedComponent<ReplyComposer> cut) => [.. cut.FindAll("ul.ts-kb-chips li .ts-kb-chip-title").Select(c => c.TextContent)];

    private static void Type(IRenderedComponent<ReplyComposer> cut, string text) => cut.Find("textarea").Input(text);

    [Fact]
    public void A_public_reply_offers_a_button_to_link_an_article_and_mounts_no_picker_and_makes_no_knowledge_base_call()
    {
        var cut = RenderComposer();

        var button = cut.Find("button.ts-composer-link-article");

        button.TextContent.Trim().ShouldBe("Link a knowledge base article");
        button.GetAttribute("aria-expanded").ShouldBe("false");
        cut.FindAll(".ts-kb-picker").ShouldBeEmpty();
        cut.FindAll("ul.ts-kb-chips").ShouldBeEmpty();
        _kb.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void The_button_opens_and_closes_the_picker_and_says_which_it_will_do()
    {
        var cut = RenderComposer();

        OpenPicker(cut);

        cut.Find("button.ts-composer-link-article").GetAttribute("aria-expanded").ShouldBe("true");
        cut.Find("button.ts-composer-link-article").TextContent.Trim().ShouldBe("Hide the article search");
        cut.Find("button.ts-composer-link-article").GetAttribute("aria-controls").ShouldBe(cut.Find(".ts-kb-picker").ParentElement!.Id);

        OpenPicker(cut);

        cut.FindAll(".ts-kb-picker").ShouldBeEmpty();
    }

    [Fact]
    public void The_picker_searches_the_tickets_product_and_the_shared_articles()
    {
        var productId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c3");
        var cut = RenderComposer(productId);
        OpenPicker(cut);

        cut.Find(".ts-kb-picker input[type=search]").Input("password");
        Time.Advance(KbDefaults.PickerDebounce);

        cut.WaitForAssertion(() => Searches().ShouldBe([new ListKbArticlesRequest(productId, false, true, KbArticleStatuses.Published, null, "password", 1, 10)]));
    }

    [Fact]
    public void A_chosen_article_becomes_a_chip_in_the_draft_and_remove_takes_it_out()
    {
        var cut = RenderComposer();
        OpenPicker(cut);

        SearchAndAdd(cut, "Reset your password", "Welcome");

        Chips(cut).ShouldBe(["Reset your password", "Welcome"]);
        Drafts.Get(TestData.TicketId).LinkedArticles.Select(a => a.Id).ShouldBe([FirstArticle, SecondArticle]);
        cut.Find("ul.ts-kb-chips").GetAttribute("aria-labelledby").ShouldBe(cut.Find("p.ts-composer-articles-label").Id);
        cut.Find("p.ts-composer-articles-label").TextContent.ShouldBe("Linked articles");

        cut.Find("ul.ts-kb-chips li button").Click();

        Chips(cut).ShouldBe(["Welcome"]);
        Drafts.Get(TestData.TicketId).LinkedArticles.Select(a => a.Id).ShouldBe([SecondArticle]);
    }

    [Fact]
    public void A_remove_button_names_the_article_it_removes()
    {
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");

        cut.Find("ul.ts-kb-chips li button").GetAttribute("aria-label").ShouldBe("Remove Welcome");
    }

    [Fact]
    public void Sending_a_public_reply_sends_the_chosen_ids_in_the_order_they_were_chosen_and_takes_them_out_of_the_draft()
    {
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome", "Reset your password");
        Type(cut, "Try this article");

        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => ReplyRequests().Count().ShouldBe(1));
        ReplyRequests().Single().LinkedArticleIds.ShouldBe([SecondArticle, FirstArticle]);
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-chips").ShouldBeEmpty());
        Drafts.Get(TestData.TicketId).LinkedArticles.ShouldBeEmpty();
    }

    [Fact]
    public void A_reply_with_no_article_chosen_sends_an_empty_list_as_before()
    {
        var cut = RenderComposer();
        Type(cut, "Plain reply");

        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => ReplyRequests().Single().LinkedArticleIds.ShouldBe([]));
    }

    [Fact]
    public void A_failed_send_keeps_the_text_and_the_chosen_articles()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>("api-error", "down"));
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");
        Type(cut, "Try this article");

        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-composer-error").ShouldNotBeNull());
        Chips(cut).ShouldBe(["Welcome"]);
        Drafts.Get(TestData.TicketId).PublicText.ShouldBe("Try this article");
    }

    // Review Focus 5: the API refuses a link to an unpublished or other-product article; the text and the choice stay.
    [Theory]
    [InlineData(ApiErrorCodes.KbArticleNotLinkable, ResultErrorKind.Validation)]
    [InlineData(ApiErrorCodes.ArticleNotFound, ResultErrorKind.NotFound)]
    public void An_article_the_api_will_not_link_keeps_the_text_and_the_choice_and_says_what_to_do(string code, ResultErrorKind kind)
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(code, "from the API", kind));
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");
        Type(cut, "Try this article");

        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-composer-error").TextContent.ShouldBe(ReplyComposerCopy.ArticleNotLinkable));
        cut.Find(".ts-composer-error").TextContent.ShouldContain("Remove the articles you no longer want, then send again. Your text is kept.");
        Chips(cut).ShouldBe(["Welcome"]);
        Drafts.Get(TestData.TicketId).PublicText.ShouldBe("Try this article");
    }

    [Fact]
    public void A_note_hides_the_article_controls_sends_no_articles_and_the_choice_is_still_there_in_a_public_reply()
    {
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");

        cut.FindAll(".ts-composer-modes button")[1].Click();

        cut.FindAll(".ts-composer-articles").ShouldBeEmpty();
        Type(cut, "Internal only");
        cut.Find(".ts-composer-actions button.btn-primary").Click();
        cut.WaitForAssertion(() => _tickets.Received(1).AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>()));
        ReplyRequests().ShouldBeEmpty();

        cut.FindAll(".ts-composer-modes button")[0].Click();

        Chips(cut).ShouldBe(["Welcome"]);
    }

    [Fact]
    public void The_chosen_articles_survive_the_screen_being_closed_and_opened_again()
    {
        var first = RenderComposer();
        OpenPicker(first);
        SearchAndAdd(first, "Welcome");
        first.Dispose();

        var second = RenderComposer();

        Chips(second).ShouldBe(["Welcome"]);
    }

    [Fact]
    public async Task An_article_chosen_while_a_send_is_on_its_way_stays_for_the_next_reply()
    {
        var pending = new TaskCompletionSource<Result<AgentMessageResponse>>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");
        Type(cut, "Try this article");
        var sending = Task.Run(() => cut.Find(".ts-composer-actions button.btn-primary").Click(), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => ReplyRequests().Count().ShouldBe(1));

        // The draft is shared, so another composer (or this one, through the store) can add while the first send is on its way.
        Drafts.Get(TestData.TicketId).LinkedArticles.Add(new ArticleChoice(FirstArticle, "Reset your password", false));
        pending.SetResult(Accepted(MessageVisibilities.Public));
        await sending;

        ReplyRequests().Single().LinkedArticleIds.ShouldBe([SecondArticle]);
        cut.WaitForAssertion(() => Drafts.Get(TestData.TicketId).LinkedArticles.Select(a => a.Id).ShouldBe([FirstArticle]));
    }

    [Fact]
    public void While_a_send_is_on_its_way_the_chips_and_the_button_cannot_be_used()
    {
        var draft = Drafts.Get(TestData.TicketId);
        draft.LinkedArticles.Add(new ArticleChoice(SecondArticle, "Welcome", true));
        draft.InFlight = true;
        draft.InFlightMode = ComposerMode.PublicReply;

        var cut = RenderComposer();

        cut.Find("ul.ts-kb-chips li button").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("button.ts-composer-link-article").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task The_draft_never_holds_more_than_ten_articles_or_the_same_article_twice_whatever_the_picker_reports()
    {
        var cut = RenderComposer();
        OpenPicker(cut);
        var picker = cut.FindComponent<ArticlePicker>().Instance;

        await cut.InvokeAsync(() => picker.OnAdd.InvokeAsync(new ArticleChoice(SecondArticle, "Welcome", true)));
        await cut.InvokeAsync(() => picker.OnAdd.InvokeAsync(new ArticleChoice(SecondArticle, "Welcome", true)));
        for (var i = 0; i < 12; i++)
        {
            await cut.InvokeAsync(() => picker.OnAdd.InvokeAsync(new ArticleChoice(Guid.NewGuid(), $"Article {i}", false)));
        }

        Drafts.Get(TestData.TicketId).LinkedArticles.Count.ShouldBe(TicketOperationLimits.MaxLinkedArticles);
        Drafts.Get(TestData.TicketId).LinkedArticles.Count(a => a.Id == SecondArticle).ShouldBe(1);
        Chips(cut).Count.ShouldBe(TicketOperationLimits.MaxLinkedArticles);
    }
}
