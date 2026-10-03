using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class UpdateMyProfileRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly Agent _me = Agent.Create("me", "Riley Chen", "riley@example.com", AgentRole.Agent, new FakeTimeProvider()).Value;

    public UpdateMyProfileRequestHandlerTests()
    {
        _claims.Current.Returns(new AgentClaims("me", "Riley Chen", "riley@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("me", Arg.Any<CancellationToken>()).Returns(_me);
    }

    private UpdateMyProfileRequestHandler Handler() => new(_claims, _agents, UnitOfWorkSubstitute.Create());

    [Theory]
    [InlineData("Ry", "Ry")]
    [InlineData("  Ry  ", "Ry")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public async Task Setting_changing_and_clearing_updates_only_the_callers_record(string? value, string? stored)
    {
        _me.SetPublicDisplayName("Before");

        var result = await Handler().HandleAsync(new UpdateMyProfileRequest(value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _me.PublicDisplayName.ShouldBe(stored);
        _agents.Received(1).Update(_me);
    }

    [Theory]
    [InlineData("ry@example.com", "public-display-name-invalid")]
    [InlineData("This display name is far too long to fit on a ticket reply line at all", "public-display-name-too-long")]
    public async Task Invalid_names_are_field_errors(string value, string code)
    {
        var result = await Handler().HandleAsync(new UpdateMyProfileRequest(value), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe(code),
            error => error.Target.ShouldBe("public-display-name"));
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
    }

    [Fact]
    public async Task A_deactivated_agent_cannot_change_their_profile()
    {
        _me.SetActive(false);

        (await Handler().HandleAsync(new UpdateMyProfileRequest("Ry"), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
    }
}
