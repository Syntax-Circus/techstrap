namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class TagRecord
{
    public Guid Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Colour { get; set; } = string.Empty;
}
