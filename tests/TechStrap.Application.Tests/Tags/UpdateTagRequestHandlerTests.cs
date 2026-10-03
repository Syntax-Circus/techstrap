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

public sealed class UpdateTagRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITagRepository _tags = Substitute.For<ITagRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Tag _tag;

    public UpdateTagRequestHandlerTests()
    {
        var admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(admin);
        _tag = Tag.Create("bug", "Bug", "#DC2626", _clock).Value;
        _tags.GetByIdAsync(_tag.Id, Arg.Any<CancellationToken>()).Returns(_tag);
    }

    private UpdateTagRequestHandler Handler() => new(_claims, _agents, _tags, _events, UnitOfWorkSubstitute.Create(), _clock);

    [Fact]
    public async Task Name_and_colour_change_and_are_audited()
    {
        var result = await Handler().HandleAsync(_tag.Id, new UpdateTagRequest("Defect", "#2563eb"), TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Name.ShouldBe("Defect"),
            dto => dto.Colour.ShouldBe("#2563EB"));
        _tags.Received(1).Update(_tag);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.Type == AdminEventType.TagUpdated && e.PayloadJson == "{\"slug\":\"bug\",\"changed\":[\"name\",\"colour\"]}"));
    }

    [Fact]
    public async Task An_unchanged_update_succeeds_and_adds_no_audit_event()
    {
        var result = await Handler().HandleAsync(_tag.Id, new UpdateTagRequest("Bug", "#dc2626"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
        _tags.DidNotReceive().Update(Arg.Any<Tag>());
    }

    [Fact]
    public async Task An_unknown_tag_is_not_found()
    {
        var result = await Handler().HandleAsync(Guid.CreateVersion7(), new UpdateTagRequest("Bug", "#DC2626"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task A_bad_colour_is_a_validation_error_on_colour()
    {
        var result = await Handler().HandleAsync(_tag.Id, new UpdateTagRequest("Bug", "red"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("colour"));
    }
}
