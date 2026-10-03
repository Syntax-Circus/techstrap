using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace TechStrap.Api.Security;

/// <summary>
/// Turns a refusal from AgentAccessAuthorizationHandler into a 403 problem with a stable type code, so the Admin app can tell
/// "not in the group" from "deactivated". Other outcomes keep the framework behaviour.
/// </summary>
public sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly Dictionary<string, string> Details = new(StringComparer.Ordinal)
    {
        [AgentAccessAuthorizationHandler.AccessRequired] = "Your account is not in the TechStrap agent or admin group. Ask your identity provider administrator to add you.",
        [AgentAccessAuthorizationHandler.AdminRequired] = "This needs the TechStrap admin group. Ask an admin to make the change, or to add you to the group.",
        [AgentAccessAuthorizationHandler.Inactive] = "Your TechStrap access is turned off. Ask an admin to reactivate it.",
    };

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var reason = authorizeResult.AuthorizationFailure?.FailureReasons
            .FirstOrDefault(failure => failure.Handler is AgentAccessAuthorizationHandler);
        if (authorizeResult.Forbidden && reason is not null && Details.TryGetValue(reason.Message, out var detail))
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: detail, type: reason.Message)
                .ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
