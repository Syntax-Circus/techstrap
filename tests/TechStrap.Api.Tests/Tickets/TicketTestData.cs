using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using TechStrap.Contracts.Tickets;
using TechStrap.Api.Tests.Auth;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Api.Tests.Tickets;

/// <summary>
/// What <see cref="TicketTestData.SeedAsync"/> created. Tickets in order: 0 Login broken (Orbitly, unassigned, tagged Billing),
/// 1 Refund request (Orbitly, unassigned), 2 Export fails (Paperplane, Sam), 3 Dark mode (Orbitly, Sam), 4 Kim's ticket (Orbitly, Kim),
/// 5 Win a prize (Orbitly, unassigned, spam). All are New.
/// Ticket.Version is not the persisted row version; use TicketTestData.VersionAsync.
/// </summary>
internal sealed record TicketSeed(Product Orbitly, Product Paperplane, Requester Ann, Agent Sam, Agent Kim, Tag Billing, IReadOnlyList<Ticket> Tickets);

/// <summary>Seeds products, agents, a requester, a tag and tickets through the real repositories (no plaintext secrets involved).</summary>
internal static class TicketTestData
{
    /// <summary>Signs Sam and Kim in via GET /api/agents/me so their agent rows exist, then seeds the tickets.</summary>
    public static async Task<TicketSeed> SeedAsync(ApiFactory factory, CancellationToken cancellationToken)
    {
        foreach (var subject in new[] { "sam", "kim" })
        {
            using var client = AgentClient(factory, subject);
            (await client.GetAsync("/api/agents/me", cancellationToken)).EnsureSuccessStatusCode();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var clock = provider.GetRequiredService<TimeProvider>();
        var products = provider.GetRequiredService<IProductRepository>();
        var agents = provider.GetRequiredService<IAgentRepository>();
        var tickets = provider.GetRequiredService<ITicketRepository>();
        var allocator = provider.GetRequiredService<ITicketNumberAllocator>();

        var sam = (await agents.GetBySubjectAsync("sam", cancellationToken))!;
        var kim = (await agents.GetBySubjectAsync("kim", cancellationToken))!;
        var orbitly = Product.Create("orbitly", "Orbitly", "ORB", null, clock).Value;
        var paperplane = Product.Create("paperplane", "Paperplane", "PPL", null, clock).Value;
        var ann = Requester.Create("ann@example.com", "Ann", null, clock).Value;
        var billing = Tag.Create("billing", "Billing", "#DC2626", clock).Value;
        var system = Actor.ForAgent(sam.Id);

        // The number allocator needs the products to exist, so they are committed first.
        await using (var setup = await provider.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken))
        {
            products.Add(orbitly);
            products.Add(paperplane);
            provider.GetRequiredService<IRequesterRepository>().Add(ann);
            provider.GetRequiredService<ITagRepository>().Add(billing);
            (await setup.CommitAsync(cancellationToken)).IsSuccess.ShouldBeTrue();
        }

        await using var work = await provider.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);

        var created = new List<Ticket>();
        async Task AddAsync(Product product, string subject, Action<Ticket>? change = null)
        {
            var number = (await allocator.AllocateAsync(product.Id, cancellationToken)).Value;
            var ticket = Ticket.Create(number, product.Id, ann.Id, subject, TicketChannel.Web, null, false, clock).Value;
            ticket.AddCustomerReply(ann.Id, $"<p>{subject}</p>", clock).IsSuccess.ShouldBeTrue();
            change?.Invoke(ticket);
            tickets.Add(ticket);
            created.Add(ticket);
        }

        await AddAsync(orbitly, "Login broken", t => t.AddTag(billing.Id, system, clock));
        await AddAsync(orbitly, "Refund request");
        await AddAsync(paperplane, "Export fails", t => t.Assign(sam.Id, system, clock));
        await AddAsync(orbitly, "Dark mode", t => t.Assign(sam.Id, system, clock));
        await AddAsync(orbitly, "Kim's ticket", t => t.Assign(kim.Id, system, clock));
        await AddAsync(orbitly, "Win a prize", t => t.MarkSpam(true, system, clock));

        (await work.CommitAsync(cancellationToken)).IsSuccess.ShouldBeTrue();
        return new TicketSeed(orbitly, paperplane, ann, sam, kim, billing, created);
    }

    /// <summary>The ticket's current persisted row version, read through the API.</summary>
    public static async Task<uint> VersionAsync(HttpClient client, Guid ticketId)
    {
        using var response = await client.GetAsync($"/api/tickets/{ticketId}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TicketDetailDto>(TestContext.Current.CancellationToken))!.RowVersion;
    }

    /// <summary>A client signed in as "sam" or "kim" (agent group). Call GET /api/agents/me once before acting; <see cref="SeedAsync"/> does.</summary>
    public static HttpClient AgentClient(ApiFactory factory, string subject) =>
        factory.CreateClient().Bearer(TestJwt.Token(subject, [TestJwt.AgentGroup], email: $"{subject}@example.com", name: subject switch
        {
            "sam" => "Sam",
            "kim" => "Kim",
            _ => throw new ArgumentException($"Unknown test agent \"{subject}\"; use \"sam\" or \"kim\".", nameof(subject)),
        }));
}
