using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Handler boundary rules from _template APPLICATION_ARCHITECTURE.md. Each method is a pure function
/// over a set of types so the tests can run it against both the real assemblies (which must pass)
/// and deliberately bad fixture types (which must fail).
/// </summary>
public static class HandlerRules
{
    public const string HandlerSuffix = "Handler";
    public const string HandleMethodName = "HandleAsync";

    /// <summary>Namespaces a handler constructor must never depend on (EF, MVC, HTTP and Npgsql types).</summary>
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore.Mvc",
        "Microsoft.AspNetCore.Http",
        "Npgsql",
    ];

    public static bool IsHandlerClass(Type type) =>
        type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith(HandlerSuffix, StringComparison.Ordinal);

    public static IReadOnlyList<string> FindShapeViolations(IEnumerable<Type> types)
    {
        var violations = new List<string>();

        foreach (var type in types.Where(IsHandlerClass).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (!type.IsSealed)
            {
                violations.Add($"{type.Name} must be sealed.");
            }

            var expectedInterfaceName = "I" + type.Name;
            var handlerInterface = type.GetInterfaces().FirstOrDefault(i => i.Name == expectedInterfaceName);
            if (handlerInterface is null)
            {
                violations.Add($"{type.Name} must implement {expectedInterfaceName}.");
                continue;
            }

            if (!handlerInterface.IsPublic && !handlerInterface.IsNestedPublic)
            {
                violations.Add($"{expectedInterfaceName} must be public.");
            }

            var handleMethods = handlerInterface.GetMethods().Where(m => m.Name == HandleMethodName).ToList();
            if (handleMethods.Count == 0)
            {
                violations.Add($"{expectedInterfaceName} must declare {HandleMethodName}.");
            }

            foreach (var method in handleMethods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[^1].ParameterType != typeof(CancellationToken))
                {
                    violations.Add($"{expectedInterfaceName}.{HandleMethodName} must take a CancellationToken as its last parameter.");
                }
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> FindConstructorDependencyViolations(
        IEnumerable<Type> types,
        Assembly infrastructureAssembly)
    {
        var violations = new List<string>();

        foreach (var type in types.Where(IsHandlerClass).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    foreach (var dependency in Flatten(parameter.ParameterType))
                    {
                        if (IsForbidden(dependency, infrastructureAssembly))
                        {
                            violations.Add($"{type.Name} must not depend on {dependency.FullName}.");
                        }
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>Controllers take handlers through [FromServices] action parameters, never the constructor.</summary>
    public static IReadOnlyList<string> FindControllerInjectionViolations(IEnumerable<Type> types)
    {
        var violations = new List<string>();

        var controllers = types
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var controller in controllers)
        {
            foreach (var constructor in controller.GetConstructors())
            {
                foreach (var parameter in constructor.GetParameters().Where(p => IsHandlerType(p.ParameterType)))
                {
                    violations.Add($"{controller.Name} constructor must not take {parameter.ParameterType.Name}; bind it with [FromServices] on the action.");
                }
            }

            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var action in actions)
            {
                foreach (var parameter in action.GetParameters().Where(p => IsHandlerType(p.ParameterType)))
                {
                    if (!parameter.ParameterType.IsInterface)
                    {
                        violations.Add($"{controller.Name}.{action.Name} must depend on the handler interface, not {parameter.ParameterType.Name}.");
                    }

                    if (!parameter.GetCustomAttributes(typeof(FromServicesAttribute), inherit: false).Any())
                    {
                        violations.Add($"{controller.Name}.{action.Name} parameter {parameter.Name} must use [FromServices].");
                    }
                }
            }
        }

        return violations;
    }

    private static bool IsHandlerType(Type type) =>
        type.Name.EndsWith(HandlerSuffix, StringComparison.Ordinal);

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var inner in Flatten(element))
            {
                yield return inner;
            }
        }
    }

    private static bool IsForbidden(Type dependency, Assembly infrastructureAssembly)
    {
        if (dependency.Assembly == infrastructureAssembly)
        {
            return true;
        }

        var ns = dependency.Namespace ?? string.Empty;
        return ForbiddenNamespacePrefixes.Any(prefix =>
            ns.Equals(prefix, StringComparison.Ordinal) || ns.StartsWith(prefix + ".", StringComparison.Ordinal));
    }
}
