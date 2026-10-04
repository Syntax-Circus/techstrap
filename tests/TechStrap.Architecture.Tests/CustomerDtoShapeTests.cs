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

    [Fact]
    public void The_type_walk_detects_unsafe_nested_types()
    {
        // Negative test: prove the walk catches violations when a customer DTO leaks agent types.
        var violations = WalkTypesFromRoot(typeof(UnsafeFixture)).ToList();
        violations.ShouldNotBeEmpty();
        violations.ShouldContain(v => v.Contains("MessageDto", StringComparison.Ordinal));
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

            // Every non-leaf type must be in the safe set
            if (!customerSafeTypes.Contains(type))
            {
                violations.Add($"Type {type.FullName} is not in the customer-safe set");
            }

            // Unwrap arrays and generic type arguments
            var typesToExamine = new List<Type> { type };
            if (type.IsArray)
            {
                typesToExamine.Add(type.GetElementType()!);
            }
            if (type.IsGenericType)
            {
                typesToExamine.AddRange(type.GetGenericArguments());
            }

            // Walk all properties to find types to visit
            var properties = type.GetProperties();
            foreach (var prop in properties)
            {
                var propType = prop.PropertyType;
                typesToExamine.Add(propType);

                // Unwrap arrays
                if (propType.IsArray)
                {
                    typesToExamine.Add(propType.GetElementType()!);
                }

                // Unwrap generic type arguments
                if (propType.IsGenericType)
                {
                    typesToExamine.AddRange(propType.GetGenericArguments());
                }
            }

            foreach (var typeToVisit in typesToExamine)
            {
                if (typeToVisit != null && !IsLeaf(typeToVisit))
                {
                    toVisit.Enqueue(typeToVisit);
                }
            }
        }

        return violations;
    }

    private static bool IsLeaf(Type type)
    {
        // True for types we don't need to walk further
        return type.IsPrimitive
            || type == typeof(string)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid)
            || type.IsEnum
            || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true;
    }

    private sealed record UnsafeFixture(IEnumerable<MessageDto> Leaked);

    private sealed record MessageDto(Guid Id, string BodyHtml);

    private static IEnumerable<string> Properties(Type type) => type.GetProperties().Select(p => p.Name);
}
