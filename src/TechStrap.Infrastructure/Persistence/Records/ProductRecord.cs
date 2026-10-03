namespace TechStrap.Infrastructure.Persistence.Records;

/// <summary>Row of <c>products</c>. Persistence shape only (D-026); the domain type is <c>Product</c>.</summary>
internal sealed class ProductRecord
{
    public Guid Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string NumberPrefix { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Logo { get; set; }

    public string AccentColour { get; set; } = string.Empty;

    public string? FromAddress { get; set; }

    public string? ReplyTo { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Postgres <c>xmin</c>, the optimistic concurrency token.</summary>
    public uint Version { get; set; }
}

/// <summary>
/// Row of <c>product_ticket_sequences</c>: the next ticket number to hand out for a product (D-009). It lives in its own table so
/// that taking a number never rewrites the product row and never changes the product's <c>xmin</c> concurrency token.
/// Created on the product's first ticket; only the ticket number allocator touches it.
/// </summary>
internal sealed class ProductTicketSequenceRecord
{
    public Guid ProductId { get; set; }

    /// <summary>The number the next ticket gets.</summary>
    public long NextNumber { get; set; }
}
