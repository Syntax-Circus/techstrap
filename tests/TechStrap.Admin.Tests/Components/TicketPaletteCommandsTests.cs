using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The ticket page's commands in the palette: they are there only while a ticket that allows them is on screen, they follow the ticket's state, and each one raises the same
/// shortcut as its key, so the composer, the sidebar and the actions menu run their own code. None of them is an admin command.
/// </summary>
public sealed class TicketPaletteCommandsTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();

    public TicketPaletteCommandsTests()
    {
        var products = Substitute.For<IProductsClient>();
        var agents = Substitute.For<IAgentsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(agents);
        Services.AddSingleton(tags);
        Services.AddTicketFeatures();
        Services.AddSingleton(AgentSessions.SignedIn());
        Services.AddSingleton(Substitute.For<IRequestersClient>());
    }

    private CommandRegistry Registry => Services.GetRequiredService<CommandRegistry>();

    private void Show(TicketDetailDto detail) => _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));

    private IRenderedComponent<TicketDetailPage> RenderTicket() => Render<TicketDetailPage>(p => p.Add(c => c.Number, "ORB-42"));

    private IReadOnlyList<string> TicketCommands() => Registry.Available(isAdmin: false).Where(c => c.Group == PaletteCopy.TicketGroup).Select(c => c.Id).ToList();

    [Fact]
    public void An_open_unassigned_ticket_offers_reply_note_and_assign_to_me_and_no_admin_command()
    {
        Show(TestData.Detail());

        RenderTicket();

        TicketCommands().ShouldBe(["ticket-reply", "ticket-note", "ticket-assign-me"]);
        Registry.Available(isAdmin: false).Where(c => c.Group == PaletteCopy.TicketGroup).ShouldAllBe(c => !c.AdminOnly);
    }

    [Fact]
    public void A_ticket_already_assigned_to_the_agent_does_not_offer_assign_to_me()
    {
        Show(TestData.Detail(assigneeId: AgentSessions.SamId, assigneeName: "Sam Ortiz"));

        RenderTicket();

        TicketCommands().ShouldBe(["ticket-reply", "ticket-note"]);
    }

    [Fact]
    public void A_spam_ticket_offers_not_spam_and_a_ticket_that_is_not_spam_does_not()
    {
        Show(TestData.Detail(isSpam: true));

        RenderTicket();

        TicketCommands().ShouldContain("ticket-not-spam");
    }

    [Fact]
    public void A_closed_ticket_offers_nothing_because_it_cannot_be_replied_to_or_changed()
    {
        Show(TestData.Detail(status: TicketStatuses.Closed));

        RenderTicket();

        TicketCommands().ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaving_the_ticket_takes_its_commands_out_of_the_palette()
    {
        Show(TestData.Detail());
        var cut = RenderTicket();
        TicketCommands().ShouldNotBeEmpty();

        await DisposeComponentsAsync();

        TicketCommands().ShouldBeEmpty();
    }

    [Fact]
    public void A_ticket_that_is_not_found_offers_nothing()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "No such ticket.", SyntaxCircus.Common.ResultErrorKind.NotFound));

        RenderTicket();

        TicketCommands().ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_command_raises_the_shortcut_that_does_the_same_thing_once()
    {
        Show(TestData.Detail(isSpam: true));
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.State(assigneeId: AgentSessions.SamId, rowVersion: 8)));
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.State(rowVersion: 8)));
        RenderTicket();
        var raised = new List<ShortcutAction>();
        ShortcutService.Pressed += action =>
        {
            raised.Add(action);
            return Task.CompletedTask;
        };

        foreach (var command in Registry.Available(isAdmin: false).Where(c => c.Group == PaletteCopy.TicketGroup))
        {
            await command.RunAsync();
        }

        raised.ShouldBe([ShortcutAction.Reply, ShortcutAction.Note, ShortcutAction.AssignToMe, ShortcutAction.NotSpam]);
    }

    [Fact]
    public async Task Assign_to_me_from_the_palette_assigns_the_ticket_to_the_agent_with_the_current_row_version()
    {
        Show(TestData.Detail());
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.State(assigneeId: AgentSessions.SamId, rowVersion: 8)));
        var cut = RenderTicket();

        await cut.InvokeAsync(() => Registry.Available(isAdmin: false).Single(c => c.Id == "ticket-assign-me").RunAsync());

        await _tickets.Received(1).AssignAsync(TestData.TicketId, Arg.Is<AssignTicketRequest>(r => r.AssigneeId == AgentSessions.SamId && r.RowVersion == 7u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_list_follows_the_ticket_after_a_write_changes_it()
    {
        // The first read finds the ticket unassigned; the reload that follows the write finds it assigned to the agent.
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(TestData.Detail()),
            TestData.Ok(TestData.Detail(assigneeId: AgentSessions.SamId, assigneeName: "Sam Ortiz", rowVersion: 8)));
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.State(assigneeId: AgentSessions.SamId, rowVersion: 8)));
        var cut = RenderTicket();
        TicketCommands().ShouldContain("ticket-assign-me");

        await cut.InvokeAsync(() => Registry.Available(isAdmin: false).Single(c => c.Id == "ticket-assign-me").RunAsync());

        cut.WaitForAssertion(() => TicketCommands().ShouldNotContain("ticket-assign-me"));
    }

    [Fact]
    public void Moving_from_one_ticket_to_another_swaps_the_commands_instead_of_adding_to_them()
    {
        Show(TestData.Detail(isSpam: true));
        _tickets.GetAsync("ORB-43", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(number: "ORB-43", assigneeId: AgentSessions.SamId, assigneeName: "Sam Ortiz")));
        var cut = RenderTicket();
        TicketCommands().ShouldBe(["ticket-reply", "ticket-note", "ticket-assign-me", "ticket-not-spam"]);

        cut.Render(p => p.Add(c => c.Number, "ORB-43"));

        cut.WaitForAssertion(() => TicketCommands().ShouldBe(["ticket-reply", "ticket-note"]));
    }
}
