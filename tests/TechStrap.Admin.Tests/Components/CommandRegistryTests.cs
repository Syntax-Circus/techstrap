using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 3, at the registry: the list is the one place that decides who sees what. An admin command is never in the list for an agent who is not an Admin, whoever registered it.
/// </summary>
public sealed class CommandRegistryTests : AdminComponentTest
{
    private CommandRegistry Registry => Services.GetRequiredService<CommandRegistry>();

    private static PaletteCommand Probe(string id = "probe", bool adminOnly = false, Func<Task>? run = null) =>
        new(id, "Probe " + id, "Test", run ?? (() => Task.CompletedTask), AdminOnly: adminOnly);

    [Fact]
    public void A_plain_agent_is_offered_the_queue_views_and_my_settings_and_no_admin_page()
    {
        var labels = Registry.Available(isAdmin: false).Select(c => c.Label).ToList();

        labels.ShouldBe(
        [
            "Queue: Unassigned", "Queue: Mine", "Queue: Open", "Queue: Pending", "Queue: All", "Queue: Spam",
            "My settings",
        ]);
        Registry.Available(isAdmin: false).ShouldAllBe(c => !c.AdminOnly);
    }

    [Fact]
    public void An_admin_is_offered_the_five_admin_pages_as_well()
    {
        var admin = Registry.Available(isAdmin: true).Where(c => c.AdminOnly).Select(c => c.Label).ToList();

        admin.ShouldBe(["Products", "Agents", "Tags", "Audit", "Failed emails"]);
        Registry.Available(isAdmin: true).Count.ShouldBe(Registry.Available(isAdmin: false).Count + 5);
    }

    [Fact]
    public async Task Every_built_in_command_goes_where_its_label_says()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var paths = new Dictionary<string, string>();

        foreach (var command in Registry.Available(isAdmin: true))
        {
            await command.RunAsync();
            paths[command.Id] = new Uri(navigation.Uri).AbsolutePath;
        }

        paths["go-queue-unassigned"].ShouldBe("/queue/unassigned");
        paths["go-queue-mine"].ShouldBe("/queue/mine");
        paths["go-queue-spam"].ShouldBe("/queue/spam");
        paths["go-my-settings"].ShouldBe("/account/notifications");
        paths["go-products"].ShouldBe("/settings/products");
        paths["go-agents"].ShouldBe("/settings/agents");
        paths["go-tags"].ShouldBe("/settings/tags");
        paths["go-audit"].ShouldBe("/settings/audit");
        paths["go-failed-emails"].ShouldBe("/ops/dead-letters");
    }

    [Fact]
    public void A_registered_command_is_offered_until_its_registration_is_disposed()
    {
        var registration = Registry.Register([Probe()]);

        Registry.Available(false).ShouldContain(c => c.Id == "probe");

        registration.Dispose();
        registration.Dispose();

        Registry.Available(false).ShouldNotContain(c => c.Id == "probe");
    }

    [Fact]
    public void An_admin_only_command_that_a_screen_registered_is_hidden_from_an_agent_too()
    {
        Registry.Register([Probe("plain"), Probe("secret", adminOnly: true)]);

        Registry.Available(isAdmin: false).Where(c => c.Group == "Test").Select(c => c.Id).ShouldBe(["plain"]);
        Registry.Available(isAdmin: true).Where(c => c.Group == "Test").Select(c => c.Id).ShouldBe(["plain", "secret"]);
    }

    [Fact]
    public void Two_screens_registering_do_not_remove_each_others_commands()
    {
        var first = Registry.Register([Probe("one")]);
        Registry.Register([Probe("two")]);

        first.Dispose();

        Registry.Available(false).Where(c => c.Group == "Test").Select(c => c.Id).ShouldBe(["two"]);
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData(null, 3)]
    [InlineData("   ", 3)]
    [InlineData("mine", 1)]
    [InlineData("MINE", 1)]
    [InlineData("queue", 2)]
    [InlineData("ticket reply", 1)]
    [InlineData("reply ticket", 1)]
    [InlineData("go to", 1)]
    [InlineData("nothing like this", 0)]
    public void Filtering_keeps_the_commands_that_contain_every_word_in_their_label_or_group(string? query, int expected)
    {
        IReadOnlyList<PaletteCommand> commands =
        [
            new("a", "Queue: Mine", "Go to", () => Task.CompletedTask),
            new("b", "Queue: Open", "Navigation", () => Task.CompletedTask),
            new("c", "Reply to requester", "Ticket", () => Task.CompletedTask),
        ];

        CommandRegistry.Filter(commands, query).Count.ShouldBe(expected);
    }
}
