using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Api.Tests.Customer;

internal sealed record CustomerSeed(
    Guid TicketId, string Number, string ValidToken, string RevokedToken, string ExpiredToken,
    string OtherTicketToken, string ErasedRequesterToken, Guid PublicAttachmentId, Guid InternalAttachmentId, Guid OtherTicketAttachmentId);

/// <summary>Seeds one product, three requesters, tickets, an agent named Sam Hargreaves and tokens through the real repositories and token service.</summary>
internal static class CustomerTestData
{
    public const string InternalNoteText = "INTERNAL-NOTE-SECRET";

    public static async Task<CustomerSeed> SeedAsync(ApiFactory factory, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var clock = provider.GetRequiredService<TimeProvider>();
        var products = provider.GetRequiredService<IProductRepository>();
        var agents = provider.GetRequiredService<IAgentRepository>();
        var requesters = provider.GetRequiredService<IRequesterRepository>();
        var tickets = provider.GetRequiredService<ITicketRepository>();
        var allocator = provider.GetRequiredService<ITicketNumberAllocator>();
        var tokens = provider.GetRequiredService<IAccessTokenService>();

        var product = Product.Create("orbitly", "Orbitly", "ORB", null, clock).Value;
        var sam = Agent.Create("sam", "Sam Hargreaves", "sam@example.com", AgentRole.Agent, clock).Value;
        var ann = Requester.Create("ann@example.com", "Ann", null, clock).Value;
        var bob = Requester.Create("bob@example.com", "Bob", null, clock).Value;
        var gone = Requester.Create("gone@example.com", "Gone", null, clock).Value;
        gone.Erase(clock);

        await using (var setup = await provider.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken))
        {
            products.Add(product);
            agents.Add(sam);
            requesters.Add(ann);
            requesters.Add(bob);
            requesters.Add(gone);
            (await setup.CommitAsync(cancellationToken)).IsSuccess.ShouldBeTrue();
        }

        await using var work = await provider.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);

        async Task<Ticket> NewTicketAsync(Requester requester, string subject)
        {
            var number = (await allocator.AllocateAsync(product.Id, cancellationToken)).Value;
            var ticket = Ticket.Create(number, product.Id, requester.Id, subject, TicketChannel.Web, null, false, clock).Value;
            ticket.AddCustomerReply(requester.Id, $"<p>{subject}</p>", clock).IsSuccess.ShouldBeTrue();
            return ticket;
        }

        var main = await NewTicketAsync(ann, "Login broken");
        var reply = main.AddAgentReply(sam.Id, "<p>We are on it</p>", clock).Value;
        var publicAttachment = reply.AddAttachment("log.txt", "text/plain", 10, "storage/public", clock).Value;
        var note = main.AddInternalNote(sam.Id, $"<p>{InternalNoteText}</p>", clock).Value;
        var internalAttachment = note.AddAttachment("secret.txt", "text/plain", 10, "storage/internal", clock).Value;
        var other = await NewTicketAsync(bob, "Other ticket");
        var otherReply = other.AddAgentReply(sam.Id, "<p>Other reply</p>", clock).Value;
        var otherAttachment = otherReply.AddAttachment("other.txt", "text/plain", 10, "storage/other", clock).Value;
        var erasedTicket = await NewTicketAsync(gone, "Erased ticket");
        tickets.Add(main);
        tickets.Add(other);
        tickets.Add(erasedTicket);

        string Issue(Ticket ticket, Requester requester, bool revoke = false)
        {
            var issued = tokens.Issue(ticket.Id, requester.Id).Value;
            if (revoke)
            {
                issued.Token.Revoke(clock);
            }

            tickets.AddAccessToken(issued.Token);
            return issued.PlaintextToken;
        }

        var valid = Issue(main, ann);
        var revoked = Issue(main, ann, revoke: true);
        var expiredIssued = tokens.Issue(main.Id, ann.Id).Value;
        var past = clock.GetUtcNow().AddDays(-1);
        tickets.AddAccessToken(TicketAccessToken.Restore(
            expiredIssued.Token.Id, main.Id, ann.Id, expiredIssued.Token.TokenHash, past.AddDays(-90), past, null, null));
        var otherToken = Issue(other, bob);
        var erasedToken = Issue(erasedTicket, gone);

        (await work.CommitAsync(cancellationToken)).IsSuccess.ShouldBeTrue();
        return new CustomerSeed(
            main.Id, main.Number.ToString(), valid, revoked, expiredIssued.PlaintextToken, otherToken, erasedToken,
            publicAttachment.Id, internalAttachment.Id, otherAttachment.Id);
    }
}
