using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class GetCurrentAgentRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));

    private GetCurrentAgentRequestHandler Handler(IUnitOfWork? unitOfWork = null) =>
        new(_claims, _agents, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock);

    private void SignedIn(AgentRole role, string? email = "sam@example.com", string? name = "Sam Whitfield") =>
        _claims.Current.Returns(new AgentClaims("sub-1", name, email, role));

    [Fact]
    public async Task The_first_call_provisions_the_agent_with_the_group_role()
    {
        SignedIn(AgentRole.Admin);

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Email.ShouldBe("sam@example.com"),
            dto => dto.Name.ShouldBe("Sam Whitfield"),
            dto => dto.Role.ShouldBe(AgentRoles.Admin),
            dto => dto.IsActive.ShouldBeTrue(),
            dto => dto.LastSeenAt.ShouldBe(_clock.GetUtcNow()));
        _agents.Received(1).Add(Arg.Is<Agent>(agent => agent.OidcSubject == "sub-1" && agent.Role == AgentRole.Admin));
    }

    [Fact]
    public async Task A_later_call_refreshes_name_email_and_mirrors_the_current_group_role()
    {
        var existing = Agent.Create("sub-1", "Old Name", "old@example.com", AgentRole.Admin, _clock).Value;
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(existing);
        SignedIn(AgentRole.Agent, email: "new@example.com", name: "New Name");

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Name.ShouldBe("New Name"),
            dto => dto.Email.ShouldBe("new@example.com"),
            dto => dto.Role.ShouldBe(AgentRoles.Agent));
        _agents.Received(1).Update(existing);
        _agents.DidNotReceive().Add(Arg.Any<Agent>());
    }

    [Fact]
    public async Task A_token_without_an_email_is_refused_with_a_clear_reason()
    {
        SignedIn(AgentRole.Agent, email: null);

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Forbidden),
            error => error.Code.ShouldBe("agent-email-required"),
            error => error.Message.ShouldContain("email claim"));
    }

    [Fact]
    public async Task An_over_long_identity_provider_name_is_shortened_instead_of_blocking_sign_in()
    {
        SignedIn(AgentRole.Agent, name: new string('n', 150));

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Value.Name!.Length.ShouldBe(100);
    }

    [Fact]
    public async Task An_unusable_email_is_forbidden_not_a_validation_error_the_agent_cannot_fix()
    {
        SignedIn(AgentRole.Agent, email: "not-an-email");

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Forbidden),
            error => error.Code.ShouldBe("agent-identity-invalid"));
    }

    [Fact]
    public async Task A_deactivated_agent_is_forbidden_and_nothing_is_saved()
    {
        var existing = Agent.Create("sub-1", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        existing.SetActive(false);
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(existing);
        SignedIn(AgentRole.Agent);

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
    }

    [Fact]
    public async Task A_concurrent_first_call_that_loses_the_insert_race_returns_the_winner_row()
    {
        var winner = Agent.Create("sub-1", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(null, winner);
        SignedIn(AgentRole.Agent);
        var unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.Duplicate));

        var result = await Handler(unitOfWork).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.Id.ShouldBe(winner.Id);
    }

    [Fact]
    public async Task Without_agent_claims_the_call_is_forbidden()
    {
        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-access-required");
    }
}
