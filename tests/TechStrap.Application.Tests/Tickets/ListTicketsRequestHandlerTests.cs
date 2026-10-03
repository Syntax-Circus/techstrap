using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class ListTicketsRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ITagRepository _tags = Substitute.For<ITagRepository>();
    private readonly Agent _sam;

    public ListTicketsRequestHandlerTests()
    {
        _sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(_sam);
        _tickets.ListAsync(Arg.Any<TicketQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<TicketSummary>([], 1, Paging.DefaultPageSize, 0));
        _products.ListAsync(false, Arg.Any<CancellationToken>()).Returns([]);
        _tags.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        _agents.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private ListTicketsRequestHandler Handler() => new(_claims, _agents, _tickets, _products, _tags);

    private static ListTicketsRequest Request(
        string? view = null, string? status = null, string? priority = null, string? search = null, int page = 1, int pageSize = 0) =>
        new(view, null, status, priority, null, null, null, search, page, pageSize);

    private TicketQuery ReceivedQuery() =>
        _tickets.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(ITicketRepository.ListAsync)).GetArguments()[0].ShouldBeOfType<TicketQuery>();

    [Theory]
    [InlineData("Unassigned", TicketView.Unassigned)]
    [InlineData("mine", TicketView.Mine)]
    [InlineData("SPAM", TicketView.Spam)]
    [InlineData(null, TicketView.All)]
    public async Task The_view_is_parsed_case_insensitively_and_defaults_to_all(string? view, TicketView expected)
    {
        var result = await Handler().HandleAsync(Request(view), Ct);

        result.IsSuccess.ShouldBeTrue();
        ReceivedQuery().ShouldSatisfyAllConditions(
            query => query.View.ShouldBe(expected),
            query => query.AgentId.ShouldBe(_sam.Id));
    }

    [Theory]
    [InlineData("Later")]
    [InlineData("3")]
    public async Task An_unknown_view_is_a_field_error(string view)
    {
        var result = await Handler().HandleAsync(Request(view), Ct);

        result.Errors[0].ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("view"),
            error => error.Code.ShouldBe("view-invalid"));
        await _tickets.DidNotReceiveWithAnyArgs().ListAsync(default!, Ct);
    }

    [Fact]
    public async Task Unknown_status_or_priority_filters_are_field_errors()
    {
        var status = await Handler().HandleAsync(Request(status: "Nope"), Ct);
        var priority = await Handler().HandleAsync(Request(priority: "5"), Ct);

        status.Errors[0].ShouldSatisfyAllConditions(e => e.Target.ShouldBe("status"), e => e.Code.ShouldBe("status-invalid"));
        priority.Errors[0].ShouldSatisfyAllConditions(e => e.Target.ShouldBe("priority"), e => e.Code.ShouldBe("priority-invalid"));
    }

    [Fact]
    public async Task Out_of_range_paging_is_a_field_error_and_zero_page_size_means_the_default()
    {
        var page = await Handler().HandleAsync(Request(page: 0), Ct);
        var tooBig = await Handler().HandleAsync(Request(pageSize: Paging.MaxPageSize + 1), Ct);
        var negative = await Handler().HandleAsync(Request(pageSize: -1), Ct);

        page.Errors[0].ShouldSatisfyAllConditions(e => e.Target.ShouldBe("page"), e => e.Code.ShouldBe("page-invalid"));
        tooBig.Errors[0].ShouldSatisfyAllConditions(e => e.Target.ShouldBe("pageSize"), e => e.Code.ShouldBe("page-size-invalid"));
        negative.Errors[0].Code.ShouldBe("page-size-invalid");

        (await Handler().HandleAsync(Request(pageSize: 0), Ct)).IsSuccess.ShouldBeTrue();
        ReceivedQuery().PageSize.ShouldBe(Paging.DefaultPageSize);
    }

    [Fact]
    public async Task Rows_are_enriched_with_product_assignee_and_tag_names_in_three_batch_lookups()
    {
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, _clock).Value;
        var tag = Tag.Create("billing", "Billing", "#DC2626", _clock).Value;
        var kim = Agent.Create("kim", "Kim", "kim@example.com", AgentRole.Agent, _clock).Value;
        var now = _clock.GetUtcNow();
        TicketSummary Row(string number, Guid? assignee, params Guid[] tags) =>
            new(Guid.NewGuid(), number, "Subject " + number, TicketStatus.Open, TicketPriority.Normal, product.Id, Guid.NewGuid(), "ann@example.com", "Ann", assignee, false, tags, now, now);
        _tickets.ListAsync(Arg.Any<TicketQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<TicketSummary>(
            [Row("ORB-1", kim.Id, tag.Id), Row("ORB-2", kim.Id, tag.Id, Guid.NewGuid()), Row("ORB-3", null)], 1, 25, 3));
        _products.ListAsync(false, Arg.Any<CancellationToken>()).Returns([product]);
        _tags.ListAsync(Arg.Any<CancellationToken>()).Returns([tag]);
        _agents.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([kim]);

        var page = (await Handler().HandleAsync(Request(), Ct)).Value;

        page.Items.Count.ShouldBe(3);
        page.Items[0].ShouldSatisfyAllConditions(
            row => row.ProductName.ShouldBe("Orbitly"),
            row => row.AssigneeName.ShouldBe("Kim"),
            row => row.Status.ShouldBe("Open"),
            row => row.Tags.ShouldHaveSingleItem().ShouldSatisfyAllConditions(t => t.Name.ShouldBe("Billing"), t => t.Colour.ShouldBe("#DC2626")));
        page.Items[1].Tags.Count.ShouldBe(1);
        page.Items[2].AssigneeName.ShouldBeNull();
        await _products.Received(1).ListAsync(false, Arg.Any<CancellationToken>());
        await _agents.Received(1).GetByIdsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1), Arg.Any<CancellationToken>());
        await _tags.Received(1).ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_overlong_search_is_cut_to_the_search_limit()
    {
        var result = await Handler().HandleAsync(Request(search: new string('a', DomainLimits.SearchTextMaxLength + 50)), Ct);

        result.IsSuccess.ShouldBeTrue();
        ReceivedQuery().SearchText.ShouldBe(new string('a', DomainLimits.SearchTextMaxLength));
    }

    [Fact]
    public async Task An_unprovisioned_agent_is_refused()
    {
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns((Agent?)null);

        var result = await Handler().HandleAsync(Request(), Ct);

        result.IsSuccess.ShouldBeFalse();
        await _tickets.DidNotReceiveWithAnyArgs().ListAsync(default!, Ct);
    }
}
