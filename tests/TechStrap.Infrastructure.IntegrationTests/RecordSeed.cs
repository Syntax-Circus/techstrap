using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Inserts rows through the internal persistence records, for schema-level tests that must not depend on the repositories.
/// Each method saves and returns the record.
/// </summary>
internal static class RecordSeed
{
    public static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    public static async Task<ProductRecord> ProductAsync(TechStrapDbContext context, string key = "acme", string prefix = "ACME")
    {
        var product = new ProductRecord
        {
            Id = Guid.CreateVersion7(),
            Key = key,
            Name = key,
            NumberPrefix = prefix,
            DisplayName = key,
            AccentColour = "#1F6FEB",
            IsActive = true,
        };
        context.Set<ProductRecord>().Add(product);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return product;
    }

    public static async Task<RequesterRecord> RequesterAsync(TechStrapDbContext context, string email = "ann@example.com")
    {
        var requester = new RequesterRecord { Id = Guid.CreateVersion7(), Email = email, Name = "Ann" };
        context.Set<RequesterRecord>().Add(requester);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return requester;
    }

    public static async Task<TicketRecord> TicketAsync(
        TechStrapDbContext context,
        ProductRecord product,
        RequesterRecord requester,
        string number,
        TicketStatus status = TicketStatus.New,
        bool isSpam = false)
    {
        var ticket = new TicketRecord
        {
            Id = Guid.CreateVersion7(),
            Number = number,
            ProductId = product.Id,
            RequesterId = requester.Id,
            Subject = "Cannot sign in",
            Status = status,
            Priority = TicketPriority.Normal,
            Channel = TicketChannel.Web,
            IsSpam = isSpam,
            CreatedAt = Now,
            LastActivityAt = Now,
        };
        context.Set<TicketRecord>().Add(ticket);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ticket;
    }

    public static async Task<MessageRecord> MessageAsync(TechStrapDbContext context, TicketRecord ticket, string body = "<p>hello</p>")
    {
        var message = new MessageRecord
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticket.Id,
            AuthorType = AuthorType.Requester,
            AuthorId = ticket.RequesterId,
            Visibility = MessageVisibility.Public,
            Body = body,
            CreatedAt = Now,
        };
        context.Set<MessageRecord>().Add(message);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return message;
    }

    public static async Task<TicketEventRecord> EventAsync(TechStrapDbContext context, TicketRecord ticket, TicketEventType type = TicketEventType.Created)
    {
        var ticketEvent = new TicketEventRecord
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticket.Id,
            Type = type,
            ActorType = ActorType.Requester,
            ActorId = ticket.RequesterId,
            Payload = "{\"number\":\"" + ticket.Number + "\"}",
            OccurredAt = Now,
        };
        context.Set<TicketEventRecord>().Add(ticketEvent);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ticketEvent;
    }

}
