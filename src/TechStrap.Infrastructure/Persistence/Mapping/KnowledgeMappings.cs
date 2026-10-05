using TechStrap.Domain.Knowledge;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Mapping;

internal static class KnowledgeMappings
{
    public static KbArticle ToDomain(this KbArticleRecord record) =>
        KbArticle.Restore(
            record.Id, record.ProductId, record.CategoryId, record.Slug, record.Title, record.Summary, record.BodyMarkdown, record.Status, record.AuthorId,
            record.CreatedAt, record.UpdatedAt, record.PublishedAt, record.Version);

    public static KbArticleRecord ToRecord(this KbArticle article)
    {
        var record = new KbArticleRecord
        {
            Id = article.Id,
            ProductId = article.ProductId,
            Slug = article.Slug,
            AuthorId = article.AuthorId,
            CreatedAt = article.CreatedAt,
        };
        article.CopyTo(record);
        return record;
    }

    /// <summary>Copies the mutable fields. The product, slug and author never change.</summary>
    public static void CopyTo(this KbArticle article, KbArticleRecord record)
    {
        record.CategoryId = article.CategoryId;
        record.Title = article.Title;
        record.Summary = article.Summary;
        record.BodyMarkdown = article.BodyMarkdown;
        record.Status = article.Status;
        record.UpdatedAt = article.UpdatedAt;
        record.PublishedAt = article.PublishedAt;
    }

    public static KbCategory ToDomain(this KbCategoryRecord record) =>
        KbCategory.Restore(record.Id, record.ProductId, record.Name, record.Slug, record.Description, record.SortOrder, record.Version);

    public static KbCategoryRecord ToRecord(this KbCategory category)
    {
        var record = new KbCategoryRecord { Id = category.Id, ProductId = category.ProductId, Slug = category.Slug };
        category.CopyTo(record);
        return record;
    }

    public static void CopyTo(this KbCategory category, KbCategoryRecord record)
    {
        record.Name = category.Name;
        record.Description = category.Description;
        record.SortOrder = category.SortOrder;
    }

    public static TicketArticleRecord ToRecord(this TicketArticle link) =>
        new() { TicketId = link.TicketId, MessageId = link.MessageId, ArticleId = link.ArticleId };
}
