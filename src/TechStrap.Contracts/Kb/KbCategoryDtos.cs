namespace TechStrap.Contracts.Kb;

/// <summary><paramref name="Version"/> is the concurrency token; send it back unchanged in <see cref="UpdateKbCategoryRequest"/>. A null product means shared.</summary>
public sealed record KbCategoryDto(Guid Id, Guid? ProductId, string Slug, string Name, string? Description, int SortOrder, uint Version);

/// <summary>The product and the slug are permanent. The slug <c>search</c> is reserved, and a slug already used in another scope is refused.</summary>
public sealed record CreateKbCategoryRequest(Guid? ProductId, string? Slug, string? Name, string? Description, int SortOrder);

/// <summary><paramref name="Version"/> must equal the version last read, otherwise the update is a 409.</summary>
public sealed record UpdateKbCategoryRequest(string? Name, string? Description, int SortOrder, uint Version);
