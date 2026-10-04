using Bunit;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Components;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Auth;

/// <summary>Review Focus 1, component half: no page content (and so no ticket call) exists until the API has accepted the agent.</summary>
public sealed class AgentGateTests : BunitContext
{
    private readonly IAgentsClient _agents;

    public AgentGateTests() => _agents = this.AddAgentShell();

    private IRenderedComponent<AgentGate> RenderGate(bool signedIn = true) => Render<AgentGate>(p => p
        .SignedIn(signedIn)
        .AddChildContent("<p id='content'>ticket data</p>"));

    private static Result<AgentDto> Refused(string code, ResultErrorKind kind = ResultErrorKind.Forbidden) =>
        Result<AgentDto>.Failure(new ResultError(code, "The API says no.", kind));

    [Fact]
    public void An_accepted_agent_sees_the_content()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null)));

        var cut = RenderGate();

        cut.WaitForAssertion(() => cut.Find("#content").TextContent.ShouldBe("ticket data"));
    }

    [Fact]
    public void The_content_does_not_exist_while_the_api_has_not_answered()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<Result<AgentDto>>().Task);

        var cut = RenderGate();

        cut.FindAll("#content").ShouldBeEmpty();
        cut.Find("[role=status]").TextContent.ShouldBe(GateCopy.Checking);
    }

    [Theory]
    [InlineData(ApiErrorCodes.AgentAccessRequired, "not in the techstrap-agents group")]
    [InlineData(ApiErrorCodes.AgentInactive, "deactivated")]
    [InlineData(ApiErrorCodes.AgentEmailRequired, "email address")]
    [InlineData(ApiErrorCodes.AgentIdentityInvalid, "could not be matched")]
    public void A_refused_agent_sees_the_reason_and_never_the_content(string code, string expectedText)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Refused(code));

        var cut = RenderGate();

        cut.FindAll("#content").ShouldBeEmpty();
        cut.Find("section.ts-no-access h1").TextContent.ShouldBe("You don't have access to TechStrap.");
        cut.Find("section.ts-no-access p").TextContent.ShouldContain(expectedText);
        cut.Find("section.ts-no-access form[action='/signout']").ShouldNotBeNull();
    }

    [Fact]
    public void An_expired_session_offers_sign_in_and_hides_the_content()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Refused(ApiErrorCodes.Unauthenticated, ResultErrorKind.Unauthenticated));

        var cut = RenderGate();

        cut.FindAll("#content").ShouldBeEmpty();
        cut.Find("form[action='/signin/start'] button").TextContent.ShouldBe(GateCopy.SignInAgain);
    }

    [Fact]
    public void When_the_api_is_unavailable_the_content_stays_hidden_and_retry_asks_again()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(
            Refused(ApiErrorCodes.ApiUnavailable, ResultErrorKind.Failure),
            Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null)));
        var cut = RenderGate();
        cut.FindAll("#content").ShouldBeEmpty();

        cut.Find("section.ts-gate button").Click();

        cut.WaitForAssertion(() => cut.Find("#content").TextContent.ShouldBe("ticket data"));
    }

    [Fact]
    public void An_anonymous_visitor_sees_the_content_and_the_api_is_not_called()
    {
        var cut = RenderGate(signedIn: false);

        cut.Find("#content").TextContent.ShouldBe("ticket data");
        _agents.DidNotReceiveWithAnyArgs().GetMeAsync(Xunit.TestContext.Current.CancellationToken);
    }
}
