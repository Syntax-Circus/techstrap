using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Api.Controllers;
using TechStrap.Application.Agents;
using TechStrap.Contracts.Agents;

namespace TechStrap.Api.Tests.Controllers;

public sealed class AgentsControllerTests
{
    private static readonly AgentDto Me = new(Guid.CreateVersion7(), "Sam", "sam@example.com", AgentRoles.Agent, true, null, null);

    [Fact]
    public async Task GetMe_DelegatesAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<IGetCurrentAgentRequestHandler>();
        handler.HandleAsync(cancellation.Token).Returns(Result<AgentDto>.Success(Me));

        var result = await ControllerTestContext.For<AgentsController>().GetMe(handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Me);
        await handler.Received(1).HandleAsync(cancellation.Token);
    }

    [Fact]
    public async Task Update_DelegatesTheIdAndBodyAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var id = Guid.CreateVersion7();
        var request = new UpdateAgentRequest(false);
        var handler = Substitute.For<IUpdateAgentRequestHandler>();
        handler.HandleAsync(id, request, cancellation.Token).Returns(Result<AgentDto>.Success(Me));

        var result = await ControllerTestContext.For<AgentsController>().Update(id, request, handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>();
        await handler.Received(1).HandleAsync(id, request, cancellation.Token);
    }
}
