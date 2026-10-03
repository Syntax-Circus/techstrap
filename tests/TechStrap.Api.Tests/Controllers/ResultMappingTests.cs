using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using SyntaxCircus.Common;

namespace TechStrap.Api.Tests.Controllers;

/// <summary>Every controller action maps each expected failure to the standard status and its success to the chosen response.</summary>
public sealed class ResultMappingTests
{
    public static TheoryData<string> Actions() => [.. ControllerActions.All().Select(ControllerActions.Key)];

    private static async Task<IActionResult> InvokeWithAsync(string key, ResultError? error)
    {
        var action = ControllerActions.All().Single(method => ControllerActions.Key(method) == key);
        var (handler, proxy) = HandlerProxy.For(ControllerActions.HandlerType(action));
        proxy.Respond = method => ControllerActions.ResultTask(method, error);
        return await ControllerActions.InvokeAsync(action, handler, CancellationToken.None);
    }

    [Fact]
    public void Every_action_has_an_expected_success_status() =>
        Actions().Select(row => row.Data).Order().ShouldBe(ControllerActions.ExpectedSuccess.Keys.Order());

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Success_returns_the_chosen_status(string key)
    {
        var result = await InvokeWithAsync(key, null);

        var status = result switch
        {
            IStatusCodeActionResult { StatusCode: { } code } => code,
            _ => -1,
        };
        status.ShouldBe(ControllerActions.ExpectedSuccess[key], key);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Failures_map_to_problem_details_statuses(string key)
    {
        (ResultErrorKind Kind, int Status)[] cases =
        [
            (ResultErrorKind.Validation, 400),
            (ResultErrorKind.Unauthenticated, 401),
            (ResultErrorKind.Forbidden, 403),
            (ResultErrorKind.NotFound, 404),
            (ResultErrorKind.Conflict, 409),
        ];

        foreach (var (kind, status) in cases)
        {
            var error = new ResultError("sample-code", "Sample message.", kind, kind == ResultErrorKind.Validation ? "name" : null);

            var result = (ObjectResult)await InvokeWithAsync(key, error);

            result.StatusCode.ShouldBe(status, $"{key} {kind}");
            if (kind == ResultErrorKind.Validation)
            {
                result.Value.ShouldBeOfType<ValidationProblemDetails>().Errors.ShouldContainKey("name");
            }
        }
    }
}
