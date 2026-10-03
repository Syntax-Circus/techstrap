using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AgentRepositoryTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static Agent NewAgent(PersistenceTestHost host, string subject, string name, string email, AgentRole role = AgentRole.Agent) =>
        Agent.Create(subject, name, email, role, host.Clock).Value;

    [Fact]
    public async Task An_agent_round_trips_including_the_public_display_name()
    {
        await using var host = new PersistenceTestHost(Database);
        var agent = NewAgent(host, "oidc|sam", "Sam W.", "sam@example.com");
        agent.SetPublicDisplayName("Samantha");
        agent.RecordSeen(host.Clock);
        (await host.CommitAsync(sp => { sp.GetRequiredService<IAgentRepository>().Add(agent); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();

        var loaded = await host.ReadAsync(sp => sp.GetRequiredService<IAgentRepository>().GetBySubjectAsync("oidc|sam", Ct));

        loaded.ShouldNotBeNull();
        loaded.Id.ShouldBe(agent.Id);
        loaded.PublicDisplayName.ShouldBe("Samantha");
        loaded.LastSeenAt.ShouldBe(agent.LastSeenAt);
        AgentPublicIdentity.Resolve(loaded, "Orbitly").ShouldBe("Samantha from Orbitly Support");
        (await host.ReadAsync(sp => sp.GetRequiredService<IAgentRepository>().GetByIdAsync(agent.Id, Ct))).ShouldNotBeNull();
        (await host.ReadAsync(sp => sp.GetRequiredService<IAgentRepository>().GetBySubjectAsync("nobody", Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Updating_changes_role_active_flag_and_clears_the_public_display_name()
    {
        await using var host = new PersistenceTestHost(Database);
        var agent = NewAgent(host, "oidc|sam", "Sam W.", "sam@example.com");
        agent.SetPublicDisplayName("Samantha");
        await host.CommitAsync(sp => { sp.GetRequiredService<IAgentRepository>().Add(agent); return Task.CompletedTask; });

        await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<IAgentRepository>();
            var loaded = (await repository.GetByIdAsync(agent.Id, Ct))!;
            loaded.ChangeRole(AgentRole.Admin);
            loaded.SetActive(false);
            loaded.SetPublicDisplayName(null);
            repository.Update(loaded);
        });

        var reloaded = (await host.ReadAsync(sp => sp.GetRequiredService<IAgentRepository>().GetByIdAsync(agent.Id, Ct)))!;
        reloaded.Role.ShouldBe(AgentRole.Admin);
        reloaded.IsActive.ShouldBeFalse();
        reloaded.PublicDisplayName.ShouldBeNull();
    }

    [Fact]
    public async Task A_second_agent_with_the_same_oidc_subject_is_a_duplicate_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        await host.CommitAsync(sp => { sp.GetRequiredService<IAgentRepository>().Add(NewAgent(host, "oidc|sam", "Sam", "sam@example.com")); return Task.CompletedTask; });

        var result = await host.CommitAsync(sp => { sp.GetRequiredService<IAgentRepository>().Add(NewAgent(host, "oidc|sam", "Other", "other@example.com")); return Task.CompletedTask; });

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Conflict);
    }

    [Fact]
    public async Task Listing_pages_by_name_and_can_hide_inactive_agents()
    {
        await using var host = new PersistenceTestHost(Database);
        var ann = NewAgent(host, "s1", "Ann", "ann@example.com");
        var bob = NewAgent(host, "s2", "Bob", "bob@example.com");
        var cat = NewAgent(host, "s3", "Cat", "cat@example.com");
        bob.SetActive(false);
        await host.CommitAsync(sp =>
        {
            var repository = sp.GetRequiredService<IAgentRepository>();
            repository.Add(cat);
            repository.Add(bob);
            repository.Add(ann);
            return Task.CompletedTask;
        });

        var all = await host.ReadAsync(sp => sp.GetRequiredService<IAgentRepository>().ListAsync(false, 1, 2, Ct));
        var active = await host.ReadAsync(sp => sp.GetRequiredService<IAgentRepository>().ListAsync(true, 1, 10, Ct));

        all.TotalCount.ShouldBe(3);
        all.Items.Select(a => a.Name).ShouldBe(["Ann", "Bob"]);
        active.Items.Select(a => a.Name).ShouldBe(["Ann", "Cat"]);
    }

    [Fact]
    public async Task Only_active_admins_are_counted()
    {
        await using var host = new PersistenceTestHost(Database);
        var activeAdmin = NewAgent(host, "s1", "A", "a@example.com", AgentRole.Admin);
        var inactiveAdmin = NewAgent(host, "s2", "B", "b@example.com", AgentRole.Admin);
        inactiveAdmin.SetActive(false);
        var plain = NewAgent(host, "s3", "C", "c@example.com");
        await host.CommitAsync(sp =>
        {
            var repository = sp.GetRequiredService<IAgentRepository>();
            repository.Add(activeAdmin);
            repository.Add(inactiveAdmin);
            repository.Add(plain);
            return Task.CompletedTask;
        });

        (await host.ReadAsync(sp => sp.GetRequiredService<IAgentRepository>().CountActiveAdminsAsync(Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task Notification_preferences_are_upserted_and_only_active_opted_in_agents_are_alerted()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
        var optedIn = NewAgent(host, "s1", "Ann", "ann@example.com");
        var optedOut = NewAgent(host, "s2", "Bob", "bob@example.com");
        var inactive = NewAgent(host, "s3", "Cat", "cat@example.com");
        inactive.SetActive(false);
        await host.CommitAsync(async sp =>
        {
            sp.GetRequiredService<IProductRepository>().Add(product);
            var agents = sp.GetRequiredService<IAgentRepository>();
            agents.Add(optedIn);
            agents.Add(optedOut);
            agents.Add(inactive);
            await Task.CompletedTask;
        });

        await host.CommitAsync(async sp =>
        {
            var agents = sp.GetRequiredService<IAgentRepository>();
            await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(optedIn.Id, product.Id, true), Ct);
            await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(optedOut.Id, product.Id, true), Ct);
            await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(inactive.Id, product.Id, true), Ct);
        });
        await host.CommitAsync(sp => sp.GetRequiredService<IAgentRepository>()
            .SetNotificationPreferenceAsync(new AgentNotificationPreference(optedOut.Id, product.Id, false), Ct));

        var alerted = await host.ReadAsync(sp => sp.GetRequiredService<IAgentRepository>().ListAgentsToAlertForProductAsync(product.Id, Ct));
        var preferences = await host.ReadAsync(sp => sp.GetRequiredService<IAgentRepository>().ListNotificationPreferencesAsync(optedOut.Id, Ct));

        alerted.Select(a => a.Id).ShouldBe([optedIn.Id]);
        preferences.ShouldHaveSingleItem().NotifyNewTicket.ShouldBeFalse();
    }

    [Fact]
    public async Task Updating_an_agent_that_was_not_loaded_is_a_programming_error()
    {
        await using var host = new PersistenceTestHost(Database);
        await using var scope = host.CreateScope();

        Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAgentRepository>().Update(NewAgent(host, "s", "A", "a@example.com")));
    }
}
