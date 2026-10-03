namespace TechStrap.Contracts.Tags;

public sealed record TagDto(Guid Id, string Slug, string Name, string Colour);

/// <summary>The slug is permanent once created; lower-case words separated by hyphens.</summary>
public sealed record CreateTagRequest(string? Slug, string? Name, string? Colour);

public sealed record UpdateTagRequest(string? Name, string? Colour);
