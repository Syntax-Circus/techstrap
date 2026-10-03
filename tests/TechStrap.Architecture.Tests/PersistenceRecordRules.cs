using System.Reflection;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// D-026: persistence records (<c>*Record</c>, namespace TechStrap.Infrastructure.Persistence.Records) are Infrastructure-internal.
/// Domain and Application must neither declare a type named *Record nor mention one anywhere in a signature or base type, and
/// the record classes themselves must not be public.
/// </summary>
public static class PersistenceRecordRules
{
    public const string RecordNamespace = "TechStrap.Infrastructure.Persistence.Records";

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IReadOnlyList<string> FindReferencesFromDomainOrApplication(IEnumerable<Type> types)
    {
        var violations = new List<string>();

        foreach (var type in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (IsRecord(type))
            {
                violations.Add($"{type.FullName} is named like a persistence record; records belong in Infrastructure.");
            }

            foreach (var used in TypesMentionedBy(type).SelectMany(AbstractionRules.Flatten).Where(IsRecord).Distinct())
            {
                violations.Add($"{type.FullName} must not reference persistence record {used.FullName}.");
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> FindRecordDeclarationViolations(Assembly infrastructure) =>
        FindRecordDeclarationViolations(infrastructure.GetTypes(), RecordNamespace);

    /// <summary>Pure rule over the Infrastructure assembly's types, so fixtures can prove each case fails.</summary>
    public static IReadOnlyList<string> FindRecordDeclarationViolations(IEnumerable<Type> infrastructureTypes, string recordNamespace)
    {
        var violations = new List<string>();

        foreach (var type in infrastructureTypes.Where(t => !t.IsNested).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var isNamedRecord = type.Name.EndsWith(AbstractionRules.RecordSuffix, StringComparison.Ordinal);

            if (type.Namespace == recordNamespace)
            {
                if (!isNamedRecord)
                {
                    violations.Add($"{type.Name} lives in the records namespace and must be named *Record.");
                }
            }
            else if (isNamedRecord)
            {
                violations.Add($"{type.FullName} is named *Record and must live in {recordNamespace}.");
            }

            if (isNamedRecord)
            {
                if (type.IsPublic)
                {
                    violations.Add($"{type.Name} must not be public.");
                }

                if (!type.IsSealed)
                {
                    violations.Add($"{type.Name} must be sealed (internal sealed, D-026).");
                }
            }
            else if (type.Namespace == recordNamespace && type.IsPublic)
            {
                violations.Add($"{type.Name} must not be public.");
            }
        }

        return violations;
    }

    private static bool IsRecord(Type type) =>
        !type.IsGenericParameter
        && (type.Name.EndsWith(AbstractionRules.RecordSuffix, StringComparison.Ordinal)
            || string.Equals(type.Namespace, RecordNamespace, StringComparison.Ordinal));

    private static IEnumerable<Type> TypesMentionedBy(Type type)
    {
        if (type.BaseType is { } baseType)
        {
            yield return baseType;
        }

        foreach (var contract in type.GetInterfaces())
        {
            yield return contract;
        }

        foreach (var field in type.GetFields(AllDeclared))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(AllDeclared))
        {
            yield return property.PropertyType;
        }

        foreach (var constructor in type.GetConstructors(AllDeclared))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(AllDeclared))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
