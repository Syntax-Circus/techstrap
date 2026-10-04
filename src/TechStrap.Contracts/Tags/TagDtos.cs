namespace TechStrap.Contracts.Tags;

public sealed record TagDto(Guid Id, string Slug, string Name, string Colour);

/// <summary>A tag with the number of tickets that carry it (any status, spam included). Admin only; the tag picker and the queue filters keep using <see cref="TagDto"/>.</summary>
public sealed record TagSummaryDto(Guid Id, string Slug, string Name, string Colour, int TicketCount);

/// <summary>The slug is permanent once created; lower-case words separated by hyphens.</summary>
public sealed record CreateTagRequest(string? Slug, string? Name, string? Colour);

public sealed record UpdateTagRequest(string? Name, string? Colour);
