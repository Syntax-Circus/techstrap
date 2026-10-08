namespace TechStrap.Contracts.Tags;

/// <summary>A tag that can be put on tickets.</summary>
/// <param name="Id">The tag's id.</param>
/// <param name="Slug">The permanent lower-case identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Colour">The display colour.</param>
public sealed record TagDto(Guid Id, string Slug, string Name, string Colour);

/// <summary>A tag with the number of tickets that carry it (any status, spam included). Admin only; the tag picker and the queue filters keep using <see cref="TagDto"/>.</summary>
/// <param name="Id">The tag's id.</param>
/// <param name="Slug">The permanent lower-case identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Colour">The display colour.</param>
/// <param name="TicketCount">The number of tickets that carry the tag.</param>
public sealed record TagSummaryDto(Guid Id, string Slug, string Name, string Colour, int TicketCount);

/// <summary>The slug is permanent once created; lower-case words separated by hyphens.</summary>
/// <param name="Slug">The permanent identifier; required.</param>
/// <param name="Name">The display name; required.</param>
/// <param name="Colour">The display colour; required.</param>
public sealed record CreateTagRequest(string? Slug, string? Name, string? Colour);

/// <summary>Changes a tag's display name and colour; the slug cannot change.</summary>
/// <param name="Name">The new display name; required.</param>
/// <param name="Colour">The new display colour; required.</param>
public sealed record UpdateTagRequest(string? Name, string? Colour);
