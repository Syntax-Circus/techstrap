using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tags;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Tags;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tags;

public sealed class CreateTagRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITagRepository _tags = Substitute.For<ITagRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));

    public CreateTagRequestHandlerTests()
    {
        var admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(admin);
    }

    private CreateTagRequestHandler Handler(params Result[] commits) => new(_claims, _agents, _tags, _events, UnitOfWorkSubstitute.Create(commits), _clock);

    [Fact]
    public async Task A_tag_is_created_with_an_upper_case_colour_and_audited()
    {
        var result = await Handler().HandleAsync(new CreateTagRequest("bug", "Bug", "#dc2626"), TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Slug.ShouldBe("bug"),
            dto => dto.Name.ShouldBe("Bug"),
            dto => dto.Colour.ShouldBe("#DC2626"));
        _tags.Received(1).Add(Arg.Is<Tag>(tag => tag.Slug == "bug" && tag.Colour == "#DC2626"));
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.Type == AdminEventType.TagCreated && e.PayloadJson == "{\"slug\":\"bug\"}"));
    }

    [Fact]
    public async Task A_bad_slug_is_a_validation_error_on_slug()
    {
        var result = await Handler().HandleAsync(new CreateTagRequest("Not A Slug", "Bug", "#DC2626"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("slug"));
    }

    [Fact]
    public async Task A_bad_colour_is_a_validation_error_on_colour()
    {
        var result = await Handler().HandleAsync(new CreateTagRequest("bug", "Bug", "red"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("colour"));
    }

    [Fact]
    public async Task A_duplicate_slug_is_a_conflict()
    {
        var result = await Handler(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.Duplicate))
            .HandleAsync(new CreateTagRequest("bug", "Bug", "#DC2626"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("tag-slug-taken"));
    }
}
