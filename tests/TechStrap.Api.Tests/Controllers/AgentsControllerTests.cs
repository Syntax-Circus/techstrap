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

    [Fact]
    public async Task UpdateMyProfile_Delegates_returns_204()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new UpdateMyProfileRequest("Ry");
        var handler = Substitute.For<IUpdateMyProfileRequestHandler>();
        handler.HandleAsync(request, cancellation.Token).Returns(Result.Success());

        var result = await ControllerTestContext.For<AgentsController>().UpdateMyProfile(request, handler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await handler.Received(1).HandleAsync(request, cancellation.Token);
    }

    [Fact]
    public async Task UpdateMyNotificationPreferences_Delegates_returns_204()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(Guid.CreateVersion7(), true)]);
        var handler = Substitute.For<IUpdateNotificationPreferencesRequestHandler>();
        handler.HandleAsync(request, cancellation.Token).Returns(Result.Success());

        var result = await ControllerTestContext.For<AgentsController>().UpdateMyNotificationPreferences(request, handler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await handler.Received(1).HandleAsync(request, cancellation.Token);
    }

    [Fact]
    public async Task GetMyNotificationPreferences_Delegates_returns_200()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<NotificationPreferenceDto> preferences = [new NotificationPreferenceDto(Guid.CreateVersion7(), "Orbitly", true)];
        var handler = Substitute.For<IGetMyNotificationPreferencesRequestHandler>();
        handler.HandleAsync(cancellation.Token).Returns(Result<IReadOnlyList<NotificationPreferenceDto>>.Success(preferences));

        var result = await ControllerTestContext.For<AgentsController>().GetMyNotificationPreferences(handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(preferences);
        await handler.Received(1).HandleAsync(cancellation.Token);
    }
}
