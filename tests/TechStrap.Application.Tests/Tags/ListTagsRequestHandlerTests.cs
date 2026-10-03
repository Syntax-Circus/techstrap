using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tags;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tags;

public sealed class ListTagsRequestHandlerTests
{
    [Fact]
    public async Task Tags_are_mapped_to_dtos_in_repository_order()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
        var bug = Tag.Create("bug", "Bug", "#DC2626", clock).Value;
        var billing = Tag.Create("billing", "Billing", "#2563EB", clock).Value;
        var tags = Substitute.For<ITagRepository>();
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns([billing, bug]);

        var result = await new ListTagsRequestHandler(tags).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.Select(dto => (dto.Id, dto.Slug, dto.Name, dto.Colour)).ShouldBe(
            [(billing.Id, "billing", "Billing", "#2563EB"), (bug.Id, "bug", "Bug", "#DC2626")]);
    }
}
