using TechStrap.Contracts.Tickets;

namespace TechStrap.Architecture.Tests;

public sealed class CustomerDtoShapeTests
{
    private static readonly string[] _forbidden =
        ["AgentId", "AuthorId", "Email", "Tags", "TagIds", "Events", "LastActivityAt", "AssigneeId", "AssigneeName",
         "RequesterId", "RowVersion", "Visibility", "MetadataJson", "ProductId", "StorageKey", "TokenHash"];

    [Theory]
    [InlineData(typeof(CustomerTicketDto))]
    [InlineData(typeof(CustomerMessageDto))]
    [InlineData(typeof(CustomerReplyResponse))]
    public void Customer_dtos_expose_no_internal_or_agent_private_fields(Type dto) =>
        Properties(dto).ShouldNotContain(name => _forbidden.Contains(name));

    [Fact]
    public void Every_type_reachable_from_a_customer_dto_is_customer_safe()
    {
        // walk property types recursively from CustomerTicketDto: every record type reached is one of
        // CustomerTicketDto, CustomerMessageDto, AttachmentDto (and no agent DTO such as MessageDto or TicketEventDto).
        var violations = WalkTypesFromRoot(typeof(CustomerTicketDto)).ToList();
        violations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(DirectEnumerableFixture))]
    [InlineData(typeof(NestedListInListFixture))]
    [InlineData(typeof(DictionaryOfListsFixture))]
    public void The_type_walk_detects_unsafe_types_through_nested_generics(Type fixture)
    {
        // Negative test: prove the walk catches violations when agent types are nested in System generics.
        var violations = WalkTypesFromRoot(fixture).ToList();
        violations.ShouldNotBeEmpty();
        violations.ShouldContain(v => v.Contains("MessageDto", StringComparison.Ordinal));
    }

    [Fact]
    public void The_type_walk_allows_safe_types_in_arrays()
    {
        // Positive test: arrays of safe types (like CustomerMessageDto[]) don't trigger violations.
        var violations = WalkTypesFromRoot(typeof(CustomerMessageDtoArrayFixture)).ToList();
        violations.ShouldBeEmpty();
    }

    private static IEnumerable<string> WalkTypesFromRoot(Type root)
    {
        var customerSafeTypes = new HashSet<Type> { typeof(CustomerTicketDto), typeof(CustomerMessageDto), typeof(AttachmentDto) };
        var violations = new List<string>();
        var visited = new HashSet<Type>();
        var toVisit = new Queue<Type>();
        toVisit.Enqueue(root);

        while (toVisit.Count > 0)
        {
            var type = toVisit.Dequeue();
            if (!visited.Add(type))
                continue;

            if (IsLeaf(type))
                continue;

            // Every non-leaf, non-nested type must be in the safe set
            // (nested types are test fixtures, exempt from the check)
            if (!IsTestFixture(type) && !customerSafeTypes.Contains(type))
            {
                violations.Add($"Type {type.FullName} is not in the customer-safe set");
            }

            // Walk all properties to find types to visit
            var properties = type.GetProperties();
            foreach (var prop in properties)
            {
                var propType = prop.PropertyType;

                // Flatten all nested generics and arrays to find all actual types
                foreach (var flattened in Flatten(propType))
                {
                    if (!IsLeaf(flattened))
                    {
                        toVisit.Enqueue(flattened);
                    }
                }
            }
        }

        return violations;
    }

    private static bool IsTestFixture(Type type)
    {
        // Only test fixture wrapper types (those ending with "Fixture") are exempt
        return type.Name.EndsWith("Fixture", StringComparison.Ordinal);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        // Yield the type itself
        yield return type;

        // If it's an array, yield its flattened element type
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            foreach (var flattened in Flatten(elementType))
            {
                yield return flattened;
            }
        }

        // If it's a generic type, yield the flattened version of each type argument
        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            foreach (var arg in args)
            {
                foreach (var flattened in Flatten(arg))
                {
                    yield return flattened;
                }
            }
        }
    }

    private static bool IsLeaf(Type type)
    {
        // True for types we don't need to walk further
        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTimeOffset) || type == typeof(Guid) || type.IsEnum)
            return true;

        // Arrays are wrappers; their element types are handled separately by Flatten
        if (type.IsArray)
            return true;

        var ns = type.Namespace;
        return ns == "System" || ns?.StartsWith("System.", StringComparison.Ordinal) == true;
    }

    private sealed record DirectEnumerableFixture(IEnumerable<MessageDto> Leaked);

    private sealed record NestedListInListFixture(IReadOnlyList<List<MessageDto>> Leaked);

    private sealed record DictionaryOfListsFixture(Dictionary<string, IReadOnlyList<MessageDto>> Leaked);

    private sealed record CustomerMessageDtoArrayFixture(CustomerMessageDto[] Messages);

    private sealed record MessageDto(Guid Id, string BodyHtml);

    private static IEnumerable<string> Properties(Type type) => type.GetProperties().Select(p => p.Name);
}
