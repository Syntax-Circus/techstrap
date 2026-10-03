using TechStrap.Application.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class TicketNameParserTests
{
    [Theory]
    [InlineData("New", TicketStatus.New)]
    [InlineData("open", TicketStatus.Open)]
    [InlineData("PENDING", TicketStatus.Pending)]
    [InlineData("sOlVeD", TicketStatus.Solved)]
    [InlineData("closed", TicketStatus.Closed)]
    [InlineData("pending ", TicketStatus.Pending)]
    public void Statuses_parse_in_any_case(string value, TicketStatus expected)
    {
        TicketNameParser.TryStatus(value, out var status).ShouldBeTrue();
        status.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("5")]
    [InlineData("0")]
    [InlineData("Closedx")]
    [InlineData("Open,New")]
    public void Statuses_that_are_not_defined_names_are_rejected(string? value) =>
        TicketNameParser.TryStatus(value, out _).ShouldBeFalse();

    [Theory]
    [InlineData("low", TicketPriority.Low)]
    [InlineData("NORMAL", TicketPriority.Normal)]
    [InlineData("High", TicketPriority.High)]
    [InlineData("urgent", TicketPriority.Urgent)]
    public void Priorities_parse_in_any_case(string value, TicketPriority expected)
    {
        TicketNameParser.TryPriority(value, out var priority).ShouldBeTrue();
        priority.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2")]
    [InlineData("Highest")]
    public void Priorities_that_are_not_defined_names_are_rejected(string? value) =>
        TicketNameParser.TryPriority(value, out _).ShouldBeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_view_is_the_default_view(string? value)
    {
        TicketNameParser.TryView(value, out var view).ShouldBeTrue();
        view.ShouldBe(TicketView.All);
    }

    [Theory]
    [InlineData("mine", TicketView.Mine)]
    [InlineData("Spam", TicketView.Spam)]
    [InlineData("UNASSIGNED", TicketView.Unassigned)]
    public void Views_parse_in_any_case(string value, TicketView expected)
    {
        TicketNameParser.TryView(value, out var view).ShouldBeTrue();
        view.ShouldBe(expected);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("1")]
    public void An_unknown_view_is_rejected(string value) =>
        TicketNameParser.TryView(value, out _).ShouldBeFalse();
}
