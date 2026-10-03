namespace TechStrap.Api.Tests.Controllers;

/// <summary>Every controller action passes the request's cancellation token to its handler.</summary>
public sealed class CancellationPropagationTests
{
    [Theory]
    [MemberData(nameof(ResultMappingTests.Actions), MemberType = typeof(ResultMappingTests))]
    public async Task Each_action_passes_the_request_cancellation_token_to_its_handler(string key)
    {
        using var cancellation = new CancellationTokenSource();
        var action = ControllerActions.All().Single(method => ControllerActions.Key(method) == key);
        var (handler, proxy) = HandlerProxy.For(ControllerActions.HandlerType(action));
        proxy.Respond = method => ControllerActions.ResultTask(method, null);

        await ControllerActions.InvokeAsync(action, handler, cancellation.Token);

        proxy.Calls.ShouldHaveSingleItem()[^1].ShouldBe(cancellation.Token);
    }
}
