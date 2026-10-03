using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Api controllers are thin adapters (_template APPLICATION_ARCHITECTURE): every controller carries an authorization policy and has no
/// constructor dependencies. Every action takes exactly one [FromServices] handler interface from Application and a CancellationToken,
/// and never a repository, unit of work, DbContext, Infrastructure type or persistence record.
/// </summary>
public static class ControllerBoundaryRules
{
    public static IReadOnlyList<string> FindViolations(IEnumerable<Type> controllers, Assembly handlerAssembly)
    {
        var violations = new List<string>();
        foreach (var controller in controllers.Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract))
        {
            if (controller.GetCustomAttribute<AuthorizeAttribute>() is not { Policy: { Length: > 0 } })
            {
                violations.Add($"{controller.Name} must declare [Authorize(Policy = ...)]");
            }

            if (controller.GetConstructors().Any(constructor => constructor.GetParameters().Length > 0))
            {
                violations.Add($"{controller.Name} must not take constructor dependencies");
            }

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null))
            {
                var name = $"{controller.Name}.{action.Name}";
                var parameters = action.GetParameters();
                var handlers = parameters.Count(parameter =>
                    parameter.ParameterType.IsInterface
                    && parameter.ParameterType.Name.EndsWith("Handler", StringComparison.Ordinal)
                    && parameter.ParameterType.Assembly == handlerAssembly
                    && parameter.GetCustomAttribute<FromServicesAttribute>() is not null);
                if (handlers != 1)
                {
                    violations.Add($"{name} must take exactly one [FromServices] handler interface (found {handlers})");
                }

                if (parameters.All(parameter => parameter.ParameterType != typeof(CancellationToken)))
                {
                    violations.Add($"{name} must take a CancellationToken");
                }

                foreach (var parameter in parameters.Where(parameter => IsForbidden(parameter.ParameterType)))
                {
                    violations.Add($"{name} must not take {parameter.ParameterType.Name}");
                }
            }
        }

        return violations;
    }

    private static bool IsForbidden(Type type) =>
        type.Name is "IUnitOfWork" or "IUnitOfWorkScope"
        || (type.IsInterface && type.Name.StartsWith('I') && type.Name.EndsWith("Repository", StringComparison.Ordinal))
        || type.Name.EndsWith("Record", StringComparison.Ordinal)
        || (type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ?? false)
        || (type.Namespace?.StartsWith("TechStrap.Infrastructure", StringComparison.Ordinal) ?? false);
}
