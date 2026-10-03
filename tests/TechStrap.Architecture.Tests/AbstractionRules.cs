using System.Reflection;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Rules for the Application abstractions that handlers depend on (P03-T07, D-026): no EF, HTTP or Infrastructure types,
/// no IQueryable, no persistence records in any signature, and every asynchronous method ends with a CancellationToken.
/// Pure functions over types, so the tests can run them on deliberately bad fixtures.
/// </summary>
public static class AbstractionRules
{
    public const string RecordSuffix = "Record";

    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "TechStrap.Infrastructure",
    ];

    public static bool IsAbstraction(Type type) =>
        type is { IsInterface: true } && (type.IsPublic || type.IsNestedPublic) && type.Name.StartsWith('I') && !type.IsGenericTypeDefinition;

    public static IReadOnlyList<string> FindShapeViolations(IEnumerable<Type> types)
    {
        var violations = new List<string>();

        foreach (var type in types.Where(IsAbstraction).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var used in SignatureTypes(member).SelectMany(Flatten))
                {
                    if (IsForbidden(used))
                    {
                        violations.Add($"{type.Name}.{member.Name} must not expose {used.FullName}.");
                    }
                }

                if (member is MethodInfo method && IsAsync(method) && !EndsWithCancellationToken(method))
                {
                    violations.Add($"{type.Name}.{method.Name} must take a CancellationToken as its last parameter.");
                }
            }
        }

        return violations;
    }

    private static bool IsAsync(MethodInfo method)
    {
        var returnType = method.ReturnType;
        return returnType == typeof(Task) || returnType == typeof(ValueTask)
            || (returnType.IsGenericType && (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)));
    }

    private static bool EndsWithCancellationToken(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken);
    }

    private static IEnumerable<Type> SignatureTypes(MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method:
                yield return method.ReturnType;
                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                break;
            case PropertyInfo property:
                yield return property.PropertyType;
                break;
            case EventInfo { EventHandlerType: { } handler }:
                yield return handler;
                break;
        }
    }

    internal static IEnumerable<Type> Flatten(Type type)
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

    internal static bool IsForbidden(Type type)
    {
        if (type.IsGenericParameter)
        {
            return false;
        }

        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        if (definition == typeof(IQueryable) || definition == typeof(IQueryable<>) || definition == typeof(IOrderedQueryable<>))
        {
            return true;
        }

        if (type.Name.EndsWith(RecordSuffix, StringComparison.Ordinal))
        {
            return true;
        }

        var ns = type.Namespace ?? string.Empty;
        return ForbiddenNamespacePrefixes.Any(prefix =>
            ns.Equals(prefix, StringComparison.Ordinal) || ns.StartsWith(prefix + ".", StringComparison.Ordinal));
    }
}
