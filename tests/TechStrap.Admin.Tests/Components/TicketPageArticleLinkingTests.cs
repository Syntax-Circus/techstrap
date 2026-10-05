using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The ticket page hands its product to the reply composer, so the picker offers that product's published articles and the shared ones, and the reply carries the chosen ids (PHASE-08 T20).</summary>
public sealed class TicketPageArticleLinkingTests : AdminComponentTest
{
    private static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-0000000000d1");

    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IKbClient _kb = Substitute.For<IKbClient>();

    public TicketPageArticleLinkingTests()
    {
        var products = Substitute.For<IProductsClient>();
        var agents = Substitute.For<IAgentsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail()));
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Public), TestData.State(rowVersion: 8))));
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.KbPage([TestData.KbItem("Reset your password", "reset-password", KbArticleStatuses.Published, TestData.OrbitlyId, id: ArticleId)])));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(_kb);
        Services.AddSingleton(products);
        Services.AddSingleton(agents);
        Services.AddSingleton(tags);
        Services.AddTicketFeatures();
        Services.AddSingleton(AgentSessions.SignedIn());
        Services.AddSingleton(Substitute.For<IRequestersClient>());
    }

    [Fact]
    public void The_picker_on_a_ticket_searches_that_tickets_product_and_the_reply_carries_the_chosen_article()
    {
        var cut = Render<TicketDetailPage>(p => p.Add(c => c.Number, "ORB-42"));

        cut.Find("button.ts-composer-link-article").Click();
        cut.Find(".ts-kb-picker input[type=search]").Input("password");
        Time.Advance(KbDefaults.PickerDebounce);
        cut.WaitForAssertion(() => cut.FindAll(".ts-kb-picker-results li").Count.ShouldBe(1));
        var search = (ListKbArticlesRequest)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.ListAsync)).GetArguments()[0]!;
        search.ProductId.ShouldBe(TestData.OrbitlyId);
        search.IncludeShared.ShouldBeTrue();
        search.Status.ShouldBe(KbArticleStatuses.Published);

        cut.Find(".ts-kb-picker-results li button").Click();
        cut.Find("textarea").Input("Here is the guide");
        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync)).ShouldBe(1));
        var request = (AddAgentReplyRequest)_tickets.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync)).GetArguments()[1]!;
        request.LinkedArticleIds.ShouldBe([ArticleId]);
    }
}
