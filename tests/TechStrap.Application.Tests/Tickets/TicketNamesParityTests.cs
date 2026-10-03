using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class TicketNamesParityTests
{
    [Fact]
    public void Status_names_match_the_domain_enum() =>
        Names(typeof(TicketStatuses)).ShouldBe(Enum.GetNames<TicketStatus>(), ignoreOrder: true);

    [Fact]
    public void Priority_names_match_the_domain_enum() =>
        Names(typeof(TicketPriorities)).ShouldBe(Enum.GetNames<TicketPriority>(), ignoreOrder: true);

    [Fact]
    public void View_names_match_the_application_enum() =>
        Names(typeof(TicketViews)).ShouldBe(Enum.GetNames<TechStrap.Application.Tickets.TicketView>(), ignoreOrder: true);

    [Fact]
    public void Event_type_names_match_the_domain_enum() =>
        Names(typeof(TicketEventTypes)).ShouldBe(Enum.GetNames<TicketEventType>(), ignoreOrder: true);

    [Fact]
    public void Author_and_visibility_names_match_the_domain_enums()
    {
        Names(typeof(MessageAuthorTypes)).ShouldBe(Enum.GetNames<AuthorType>(), ignoreOrder: true);
        Names(typeof(MessageVisibilities)).ShouldBe(Enum.GetNames<MessageVisibility>(), ignoreOrder: true);
    }

    // Distinct values of every public const string, so an alias such as TicketViews.Default does not count twice.
    private static string[] Names(Type holder) =>
        [.. holder.GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!).Distinct()];
}
