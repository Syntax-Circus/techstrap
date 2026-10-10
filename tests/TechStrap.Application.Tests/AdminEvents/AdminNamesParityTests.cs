using TechStrap.Contracts.AdminEvents;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Tests.AdminEvents;

/// <summary>The audit page branches on these constants; if the Domain gains a type the constants must follow, or the page would show a generic line for a type it was told it knows.</summary>
public sealed class AdminNamesParityTests
{
    [Fact]
    public void Event_type_names_match_the_domain_enum()
    {
        Names(typeof(AdminEventTypes)).ShouldBe(Enum.GetNames<AdminEventType>(), ignoreOrder: true);
        Names(typeof(AdminEventTypes)).Length.ShouldBe(13);
    }

    [Fact]
    public void Subject_type_names_match_the_domain_enum()
    {
        Names(typeof(AdminSubjectTypes)).ShouldBe(Enum.GetNames<AdminSubjectType>(), ignoreOrder: true);
        Names(typeof(AdminSubjectTypes)).Length.ShouldBe(8);
    }

    [Fact]
    public void Every_constant_value_equals_its_own_name()
    {
        foreach (var holder in new[] { typeof(AdminEventTypes), typeof(AdminSubjectTypes) })
        {
            foreach (var field in holder.GetFields().Where(f => f.IsLiteral))
            {
                field.GetRawConstantValue().ShouldBe(field.Name);
            }
        }
    }

    // Distinct values of every public const string, the same helper shape as TicketNamesParityTests.
    private static string[] Names(Type holder) =>
        [.. holder.GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!).Distinct()];
}
