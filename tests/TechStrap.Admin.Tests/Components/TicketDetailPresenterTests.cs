using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TicketDetailPresenterTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private readonly ITagsClient _tags = Substitute.For<ITagsClient>();

    public TicketDetailPresenterTests()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail()));
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        _tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
    }

    private TicketDetailPresenter Presenter() => new(_tickets, _products, _agents, _tags);

    [Fact]
    public async Task A_ticket_detail_fixture_maps_to_the_view_model()
    {
        var tag = new TicketTagDto(TestData.BugTagId, "bug", "#DC2626");
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(
            status: TicketStatuses.Pending, priority: TicketPriorities.High, assigneeId: TestData.SamAgentId, assigneeName: "Sam Ortiz", tags: [tag],
            messages: [TestData.Message(), TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Internal, authorName: "Sam Ortiz")],
            events: [TestData.Event(TicketEventTypes.Created, """{"channel":"Email"}""")], rowVersion: 9)));

        var result = await Presenter().LoadAsync("ORB-42", Ct);

        var model = result.Value;
        (model.Number, model.Subject, model.Status, model.Priority, model.RowVersion).ShouldBe(("ORB-42", "Cannot log in", "Pending", "High", 9u));
        (model.AssigneeId, model.AssigneeName, model.ProductName, model.IsSpam, model.IsClosed).ShouldBe((TestData.SamAgentId, "Sam Ortiz", "Orbitly", false, false));
        model.Tags.ShouldBe([tag]);
        model.Caller.ShouldBe("Ada Lovelace");
        model.Stamp.ShouldBe(StampStatus.Pending);
        model.Timeline.Count.ShouldBe(3);
        model.Timeline.Count(e => e.Message is not null).ShouldBe(2);
        model.Lookups.Agents.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_closed_spam_ticket_reports_closed_and_wears_the_spam_stamp()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(status: TicketStatuses.Closed, isSpam: true)));

        var model = (await Presenter().LoadAsync("ORB-42", Ct)).Value;

        model.IsClosed.ShouldBeTrue();
        model.Stamp.ShouldBe(StampStatus.Spam);
    }

    [Fact]
    public async Task A_missing_ticket_is_reported_as_not_found_even_when_a_lookup_also_failed()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "No such ticket.", ResultErrorKind.NotFound));
        _tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<TagDto>>("boom"));

        var result = await Presenter().LoadAsync("ORB-42", Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.NotFound);
        result.Errors[0].Code.ShouldBe("ticket-not-found");
    }

    [Theory]
    [InlineData("products")]
    [InlineData("agents")]
    [InlineData("tags")]
    public async Task A_failed_lookup_fails_the_load_with_that_error(string which)
    {
        switch (which)
        {
            case "products":
                _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("products-down", "Products are unavailable."));
                break;
            case "agents":
                _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<AgentListItemDto>>("agents-down", "Agents are unavailable."));
                break;
            default:
                _tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<TagDto>>("tags-down", "Tags are unavailable."));
                break;
        }

        var result = await Presenter().LoadAsync("ORB-42", Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe($"{which}-down");
    }

    [Fact]
    public async Task A_follow_up_fetches_its_parent_once_to_show_the_parent_number()
    {
        var parentId = Guid.NewGuid();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(parentId: parentId)));
        _tickets.GetAsync(parentId.ToString(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail("ORB-7")));

        var model = (await Presenter().LoadAsync("ORB-42", Ct)).Value;

        model.ParentTicketId.ShouldBe(parentId);
        model.ParentNumber.ShouldBe("ORB-7");
        await _tickets.Received(1).GetAsync(parentId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_parent_that_is_gone_shows_no_link_and_does_not_fail_the_load()
    {
        var parentId = Guid.NewGuid();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(parentId: parentId)));
        _tickets.GetAsync(parentId.ToString(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));

        var result = await Presenter().LoadAsync("ORB-42", Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ParentNumber.ShouldBeNull();
    }

    [Fact]
    public async Task Given_lookups_to_reuse_it_fetches_only_the_ticket()
    {
        var reused = new TicketLookups([TestData.Product()], [TestData.Agent("Sam Ortiz", TestData.SamAgentId)], [TestData.Tag()]);

        var result = await Presenter().LoadAsync("ORB-42", reused, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Lookups.ShouldBeSameAs(reused);
        await _products.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        await _agents.DidNotReceive().ListAllAsync(Arg.Any<CancellationToken>());
        await _tags.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_ticket_without_a_parent_makes_no_extra_read()
    {
        await Presenter().LoadAsync("ORB-42", Ct);

        await _tickets.Received(1).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("""{"appVersion":"2.3.1","device":"Pixel 8"}""", true)]
    public async Task Metadata_is_read_into_labelled_items_and_absent_when_there_is_none(string? json, bool present)
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(metadataJson: json, metadataTrusted: true)));

        var metadata = (await Presenter().LoadAsync("ORB-42", Ct)).Value.Metadata;

        (metadata is not null).ShouldBe(present);
        if (present)
        {
            metadata!.Trusted.ShouldBeTrue();
            metadata.Items.ShouldBe([new MetadataItem("appVersion", "2.3.1"), new MetadataItem("device", "Pixel 8")]);
        }
    }

    [Fact]
    public async Task Metadata_that_is_not_valid_json_is_flagged_unreadable_not_dropped()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(metadataJson: "{oops")));

        var metadata = (await Presenter().LoadAsync("ORB-42", Ct)).Value.Metadata;

        metadata.ShouldNotBeNull();
        metadata.Readable.ShouldBeFalse();
        metadata.Trusted.ShouldBeFalse();
    }

    [Fact]
    public async Task Metadata_is_capped_at_the_item_limit()
    {
        var json = "{" + string.Join(",", Enumerable.Range(0, TicketDetailPresenter.MaxMetadataItems + 20).Select(i => $"\"k{i}\":\"v\"")) + "}";
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(metadataJson: json)));

        var metadata = (await Presenter().LoadAsync("ORB-42", Ct)).Value.Metadata;

        metadata!.Items.Count.ShouldBe(TicketDetailPresenter.MaxMetadataItems);
    }

    [Fact]
    public async Task A_long_metadata_value_is_cut_with_an_ellipsis()
    {
        var json = $$"""{"short":"ok","long":"{{new string('x', TicketDetailPresenter.MaxMetadataValueLength + 100)}}"}""";
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(metadataJson: json)));

        var items = (await Presenter().LoadAsync("ORB-42", Ct)).Value.Metadata!.Items;

        items[0].Value.ShouldBe("ok");
        items[1].Value.Length.ShouldBe(TicketDetailPresenter.MaxMetadataValueLength + 1);
        items[1].Value.ShouldEndWith("\u2026");
    }

    [Theory]
    [InlineData("products")]
    [InlineData("agents")]
    [InlineData("tags")]
    public async Task A_lookup_that_is_not_found_never_reads_as_a_missing_ticket(string which)
    {
        switch (which)
        {
            case "products":
                _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("gone", "Gone.", ResultErrorKind.NotFound));
                break;
            case "agents":
                _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<AgentListItemDto>>("gone", "Gone.", ResultErrorKind.NotFound));
                break;
            default:
                _tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<TagDto>>("gone", "Gone.", ResultErrorKind.NotFound));
                break;
        }

        var result = await Presenter().LoadAsync("ORB-42", Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldNotBe(ResultErrorKind.NotFound);
        result.Errors[0].Code.ShouldBe("gone");
    }

    [Fact]
    public async Task The_token_reaches_every_client_call()
    {
        using var cts = new CancellationTokenSource();

        await Presenter().LoadAsync("ORB-42", cts.Token);

        await _tickets.Received().GetAsync("ORB-42", cts.Token);
        await _products.Received().ListAsync(cts.Token);
        await _agents.Received().ListAllAsync(cts.Token);
        await _tags.Received().ListAsync(cts.Token);
    }

    [Fact]
    public async Task A_write_s_state_response_replaces_status_assignee_tags_and_row_version_from_the_lookups()
    {
        var model = (await Presenter().LoadAsync("ORB-42", Ct)).Value;

        var after = model.WithState(TestData.State(TicketStatuses.Solved, TicketPriorities.Urgent, TestData.SamAgentId, tagIds: [TestData.BugTagId], rowVersion: 12));

        after.Status.ShouldBe(TicketStatuses.Solved);
        after.Priority.ShouldBe(TicketPriorities.Urgent);
        after.AssigneeName.ShouldBe("Sam Ortiz");
        after.Tags.Select(t => t.Name).ShouldBe(["bug"]);
        after.RowVersion.ShouldBe(12u);
        after.Timeline.ShouldBe(model.Timeline);
    }

    [Fact]
    public async Task An_assignee_missing_from_the_lookups_keeps_the_name_already_shown_and_a_new_unknown_one_gets_a_fallback()
    {
        var detail = TestData.Detail(assigneeId: TestData.AdaAgentId, assigneeName: "Ada Admin");
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));
        var model = (await Presenter().LoadAsync("ORB-42", Ct)).Value;

        model.WithState(TestData.State(assigneeId: TestData.AdaAgentId)).AssigneeName.ShouldBe("Ada Admin");
        model.WithState(TestData.State(assigneeId: Guid.NewGuid())).AssigneeName.ShouldBe("another agent");
        model.WithState(TestData.State(assigneeId: null)).AssigneeName.ShouldBeNull();
    }
}
