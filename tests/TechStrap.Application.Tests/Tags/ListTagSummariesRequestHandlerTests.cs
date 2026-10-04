using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tags;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tags;

public sealed class ListTagSummariesRequestHandlerTests
{
    [Fact]
    public async Task Summaries_carry_the_ticket_count_and_keep_the_repository_order()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));
        var billing = Tag.Create("billing", "Billing", "#2563EB", clock).Value;
        var bug = Tag.Create("bug", "Bug", "#DC2626", clock).Value;
        var unused = Tag.Create("unused", "Unused", "#16A34A", clock).Value;
        var tags = Substitute.For<ITagRepository>();
        tags.ListWithTicketCountsAsync(Arg.Any<CancellationToken>()).Returns([new TagUsage(billing, 3), new TagUsage(bug, 12), new TagUsage(unused, 0)]);

        var result = await new ListTagSummariesRequestHandler(tags).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.Select(dto => (dto.Id, dto.Slug, dto.Name, dto.Colour, dto.TicketCount)).ShouldBe(
            [(billing.Id, "billing", "Billing", "#2563EB", 3), (bug.Id, "bug", "Bug", "#DC2626", 12), (unused.Id, "unused", "Unused", "#16A34A", 0)]);
    }

    [Fact]
    public async Task No_tags_is_an_empty_success()
    {
        var tags = Substitute.For<ITagRepository>();
        tags.ListWithTicketCountsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListTagSummariesRequestHandler(tags).HandleAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }
}
