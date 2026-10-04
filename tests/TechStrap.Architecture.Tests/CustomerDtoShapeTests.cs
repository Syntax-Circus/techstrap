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
        var customerSafeTypes = new[] { typeof(CustomerTicketDto), typeof(CustomerMessageDto), typeof(AttachmentDto) };
        var visited = new HashSet<Type>();
        var toVisit = new Queue<Type>();
        toVisit.Enqueue(typeof(CustomerTicketDto));

        while (toVisit.Count > 0)
        {
            var type = toVisit.Dequeue();
            if (!visited.Add(type))
                continue;

            // Skip primitive types, strings, DateTimeOffset, Guid, enums
            if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTimeOffset) || type == typeof(Guid) || type.IsEnum)
                continue;

            // Handle IReadOnlyList<T> by extracting the element type
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                var elementType = type.GetGenericArguments()[0];
                if (!elementType.IsPrimitive && elementType != typeof(string) && elementType != typeof(DateTimeOffset) && elementType != typeof(Guid) && !elementType.IsEnum)
                {
                    toVisit.Enqueue(elementType);
                }
                continue;
            }

            // For record types, verify they are customer-safe
            if (type.IsValueType || (type.IsClass && !type.IsValueType))
            {
                if (type.Name.EndsWith("Dto", StringComparison.Ordinal) || type.Name.EndsWith("Response", StringComparison.Ordinal))
                {
                    customerSafeTypes.ShouldContain(type, $"Type {type.FullName} is not in the customer-safe list");
                }
            }

            // Walk all properties to find types to visit
            var properties = type.GetProperties();
            foreach (var prop in properties)
            {
                var propType = prop.PropertyType;

                // Handle IReadOnlyList<T>
                if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                {
                    var elementType = propType.GetGenericArguments()[0];
                    toVisit.Enqueue(elementType);
                }
                else if (!propType.IsPrimitive && propType != typeof(string) && propType != typeof(DateTimeOffset) && propType != typeof(Guid) && !propType.IsEnum && propType != typeof(object))
                {
                    toVisit.Enqueue(propType);
                }
            }
        }
    }

    private static IEnumerable<string> Properties(Type type) => type.GetProperties().Select(p => p.Name);
}
