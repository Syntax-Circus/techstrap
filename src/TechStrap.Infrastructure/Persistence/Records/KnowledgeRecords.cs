using NpgsqlTypes;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class KbCategoryRecord
{
    public Guid Id { get; set; }

    /// <summary>Null means shared across all products.</summary>
    public Guid? ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

internal sealed class KbArticleRecord
{
    public Guid Id { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? CategoryId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string BodyMarkdown { get; set; } = string.Empty;

    public KbArticleStatus Status { get; set; }

    public Guid AuthorId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Postgres <c>xmin</c>, the optimistic concurrency token.</summary>
    public uint Version { get; set; }

    /// <summary>Generated column (D-027): title weight A, summary B, body C. Never written by the application.</summary>
    public NpgsqlTsVector SearchVector { get; set; } = null!;
}

internal sealed class TicketArticleRecord
{
    public Guid TicketId { get; set; }

    public Guid MessageId { get; set; }

    public Guid ArticleId { get; set; }
}
