using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Seeds two products, an agent and a requester through the repositories, then creates and changes tickets the way handlers will:
/// allocate a number, create the aggregate, stage it, commit once.
/// </summary>
internal sealed class TicketScenario
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private TicketScenario(PersistenceTestHost host, Product acme, Product orbitly, Agent agent, Requester requester)
    {
        Host = host;
        Acme = acme;
        Orbitly = orbitly;
        Agent = agent;
        Requester = requester;
    }

    public PersistenceTestHost Host { get; }

    public Product Acme { get; }

    public Product Orbitly { get; }

    public Agent Agent { get; }

    public Requester Requester { get; }

    public static async Task<TicketScenario> CreateAsync(PersistenceTestHost host)
    {
        var acme = Product.Create("acme", "Acme", "ACME", null, host.Clock).Value;
        var orbitly = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
        var agent = Agent.Create("oidc|sam", "Sam W.", "sam@example.com", AgentRole.Agent, host.Clock).Value;
        var requester = Requester.Create("ann@example.com", "Ann", null, host.Clock).Value;
        var result = await host.CommitAsync(sp =>
        {
            var products = sp.GetRequiredService<IProductRepository>();
            products.Add(acme);
            products.Add(orbitly);
            sp.GetRequiredService<IAgentRepository>().Add(agent);
            sp.GetRequiredService<IRequesterRepository>().Add(requester);
            return Task.CompletedTask;
        });
        result.IsSuccess.ShouldBeTrue();
        return new TicketScenario(host, acme, orbitly, agent, requester);
    }

    /// <summary>Creates a ticket with its first customer message, in one unit of work. Returns the saved ticket.</summary>
    public async Task<Ticket> CreateTicketAsync(
        string subject = "Cannot sign in",
        Product? product = null,
        Action<Ticket>? change = null,
        string firstMessage = "<p>I cannot sign in</p>")
    {
        var target = product ?? Acme;
        Ticket? created = null;
        var result = await Host.CommitAsync(async sp =>
        {
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(target.Id, Ct)).Value;
            var ticket = Ticket.Create(number, target.Id, Requester.Id, subject, TicketChannel.Web, null, false, Host.Clock).Value;
            ticket.AddCustomerReply(Requester.Id, firstMessage, Host.Clock).IsSuccess.ShouldBeTrue();
            change?.Invoke(ticket);
            sp.GetRequiredService<ITicketRepository>().Add(ticket);
            created = ticket;
        });
        result.IsSuccess.ShouldBeTrue();
        Host.Clock.Advance(TimeSpan.FromMinutes(1));
        return created!;
    }

    /// <summary>Loads the ticket, applies the change, stages the update and commits. Returns the commit result.</summary>
    public Task<Result> UpdateAsync(Guid ticketId, Action<Ticket> change) =>
        Host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            var ticket = (await repository.GetByIdAsync(ticketId, Ct))!;
            change(ticket);
            repository.Update(ticket);
        });

    public Task<Ticket?> LoadAsync(Guid ticketId) =>
        Host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetByIdAsync(ticketId, Ct));

    public Actor AgentActor => Actor.ForAgent(Agent.Id);
}
