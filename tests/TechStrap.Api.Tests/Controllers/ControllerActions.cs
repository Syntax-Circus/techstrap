using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.Common;

namespace TechStrap.Api.Tests.Controllers;

/// <summary>Finds every Api controller action and invokes it with a recording handler stand-in.</summary>
public static class ControllerActions
{
    /// <summary>The success status each action must return. Adding an action without a row here fails ResultMappingTests.</summary>
    public static readonly IReadOnlyDictionary<string, int> ExpectedSuccess = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["AgentsController.GetMe"] = 200,
        ["AgentsController.List"] = 200,
        ["AgentsController.Update"] = 200,
        ["AgentsController.GetMyNotificationPreferences"] = 200,
        ["AgentsController.UpdateMyNotificationPreferences"] = 204,
        ["AgentsController.UpdateMyProfile"] = 204,
        ["ProductsController.List"] = 200,
        ["ProductsController.Get"] = 200,
        ["ProductsController.Create"] = 201,
        ["ProductsController.Update"] = 200,
        ["ProductsController.UploadLogo"] = 200,
        ["ProductsController.RemoveLogo"] = 200,
        ["ProductsController.ListApiKeys"] = 200,
        ["ProductsController.CreateApiKey"] = 201,
        ["ProductsController.RevokeApiKey"] = 204,
        ["TagsController.List"] = 200,
        ["TagsController.ListSummaries"] = 200,
        ["TagsController.Create"] = 201,
        ["TagsController.Update"] = 200,
        ["TagsController.Delete"] = 204,
        ["TicketsController.List"] = 200,
        ["TicketsController.Counts"] = 200,
        ["TicketsController.Get"] = 200,
        ["TicketsController.Reply"] = 201,
        ["TicketsController.AddNote"] = 201,
        ["TicketsController.ChangeStatus"] = 200,
        ["TicketsController.Assign"] = 200,
        ["TicketsController.ChangePriority"] = 200,
        ["TicketsController.MoveProduct"] = 200,
        ["TicketsController.AddTag"] = 200,
        ["TicketsController.RemoveTag"] = 200,
        ["TicketsController.MarkSpam"] = 200,
        ["TicketsController.Delete"] = 204,
        ["RequestersController.Erase"] = 204,
        ["DeadLettersController.List"] = 200,
        ["DeadLettersController.Retry"] = 204,
        ["DeadLettersController.Discard"] = 204,
        ["AttachmentsController.Get"] = 200,
        ["AdminEventsController.List"] = 200,
        ["IntakeController.Submit"] = 201,
        ["PublicIntakeController.Submit"] = 201,
        ["PublicProductsController.Get"] = 200,
        ["PublicProductsController.List"] = 200,
        ["PublicSiteController.Get"] = 200,
        ["SiteSettingsController.Get"] = 200,
        ["SiteSettingsController.Put"] = 200,
        ["KbArticlesController.List"] = 200,
        ["KbArticlesController.Get"] = 200,
        ["KbArticlesController.Create"] = 201,
        ["KbArticlesController.Update"] = 200,
        ["KbArticlesController.Publish"] = 200,
        ["KbArticlesController.Archive"] = 200,
        ["KbCategoriesController.List"] = 200,
        ["KbCategoriesController.Create"] = 201,
        ["KbCategoriesController.Update"] = 200,
        ["KbCategoriesController.Delete"] = 204,
        ["KbPreviewController.Render"] = 200,
        ["KbImagesController.Upload"] = 201,
        ["PublicKbController.Search"] = 200,
        ["PublicKbController.Categories"] = 200,
        ["PublicKbController.CategoryArticles"] = 200,
        ["PublicKbController.Article"] = 200,
        ["PublicKbController.Sitemap"] = 200,
        ["CustomerTicketsController.Get"] = 200,
        ["CustomerTicketsController.Reply"] = 201,
        ["CustomerTicketsController.GetAttachment"] = 200,
        ["CustomerAccessLinkController.RequestLink"] = 202,
    };

    /// <summary>Actions whose effective policy (action-level, else controller-level) is Agent or Admin.</summary>
    public static IEnumerable<MethodInfo> AgentOrAdminActions() =>
        All().Where(method =>
            (method.GetCustomAttribute<AuthorizeAttribute>() ?? method.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>())?.Policy
                is TechStrap.Api.Security.AuthorizationPolicies.Agent or TechStrap.Api.Security.AuthorizationPolicies.Admin);

    public static IEnumerable<MethodInfo> All() =>
        typeof(TechStrap.Api.Program).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);

    public static string Key(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    public static Type HandlerType(MethodInfo action) =>
        action.GetParameters().Single(parameter => parameter.GetCustomAttribute<FromServicesAttribute>() is not null).ParameterType;

    /// <summary>Invokes the action with sample arguments, the given handler and token; returns the action result. Exceptions propagate to the caller.</summary>
    public static async Task<IActionResult> InvokeAsync(MethodInfo action, object handler, CancellationToken cancellationToken)
    {
        var controller = (ControllerBase)typeof(ControllerTestContext).GetMethod(nameof(ControllerTestContext.For))!
            .MakeGenericMethod(action.DeclaringType!).Invoke(null, null)!;
        var arguments = action.GetParameters().Select(parameter => Argument(parameter, handler, cancellationToken)).ToArray();
        return await (Task<IActionResult>)action.Invoke(controller, arguments)!;
    }

    /// <summary>A completed Task of the handler's result type: success with a sample value, or failure with the error.</summary>
    public static object ResultTask(MethodInfo handleAsync, ResultError? error)
    {
        var resultType = handleAsync.ReturnType.GetGenericArguments()[0];
        object result;
        if (resultType == typeof(Result))
        {
            result = error is null ? Result.Success() : Result.Failure(error);
        }
        else
        {
            result = error is null
                ? resultType.GetMethod(nameof(Result<int>.Success))!.Invoke(null, [Sample(resultType.GetGenericArguments()[0])])!
                : resultType.GetMethod(nameof(Result<int>.Failure))!.Invoke(null, [error, Array.Empty<ResultError>()])!;
        }

        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [result])!;
    }

    private static object? Argument(ParameterInfo parameter, object handler, CancellationToken cancellationToken)
    {
        var type = parameter.ParameterType;
        if (parameter.GetCustomAttribute<FromServicesAttribute>() is not null)
        {
            return handler;
        }

        if (type == typeof(CancellationToken))
        {
            return cancellationToken;
        }

        if (type == typeof(Guid))
        {
            return Guid.CreateVersion7();
        }

        if (type == typeof(int))
        {
            return 1;
        }

        if (type == typeof(bool))
        {
            return true;
        }

        return Nullable.GetUnderlyingType(type) is not null || type == typeof(string) ? null : Sample(type);
    }

    private static object Sample(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        }

        return RuntimeHelpers.GetUninitializedObject(type);
    }
}

/// <summary>A stand-in for any handler interface: records each call's arguments and returns what <see cref="Respond"/> builds.</summary>
public class HandlerProxy : DispatchProxy
{
    public Func<MethodInfo, object?> Respond { get; set; } = _ => null;

    public List<object?[]> Calls { get; } = [];

    public static (object Handler, HandlerProxy Proxy) For(Type handlerInterface)
    {
        var created = DispatchProxy.Create(handlerInterface, typeof(HandlerProxy));
        return (created, (HandlerProxy)created);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add(args ?? []);
        return Respond(targetMethod!);
    }
}
