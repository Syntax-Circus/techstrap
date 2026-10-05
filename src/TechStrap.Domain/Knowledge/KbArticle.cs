using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Knowledge;

public enum KbArticleStatus
{
    Draft,
    Published,
    Archived,
}

/// <summary>
/// A knowledge-base article written in Markdown. Draft goes to Published (or Archived); Published goes to Archived; Archived
/// can be published again, and editing an Archived article returns it to Draft (D-044). Publishing needs a title, a slug, a body and a
/// category, because the portal URL carries the category slug. The slug is unique within its product (shared articles have a
/// null product and their own slug space); the handlers also block a slug that is taken in the other scope (D-044).
/// </summary>
public sealed class KbArticle
{
    /// <summary>The code of the Validation error <see cref="Publish"/> returns when a required field is missing; its target names the field (title, slug, body or category).</summary>
    public const string PublishIncompleteCode = "kb-publish-incomplete";

    private KbArticle(
        Guid id,
        Guid? productId,
        Guid? categoryId,
        string slug,
        string title,
        string? summary,
        string bodyMarkdown,
        KbArticleStatus status,
        Guid authorId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? publishedAt,
        uint version)
    {
        Id = id;
        ProductId = productId;
        CategoryId = categoryId;
        Slug = slug;
        Title = title;
        Summary = summary;
        BodyMarkdown = bodyMarkdown;
        Status = status;
        AuthorId = authorId;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        PublishedAt = publishedAt;
        Version = version;
    }

    public Guid Id { get; }

    /// <summary>Null means shared across all products.</summary>
    public Guid? ProductId { get; }

    public Guid? CategoryId { get; private set; }

    public string Slug { get; }

    public string Title { get; private set; }

    public string? Summary { get; private set; }

    public string BodyMarkdown { get; private set; }

    public KbArticleStatus Status { get; private set; }

    public Guid AuthorId { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Opaque optimistic-concurrency token as loaded (Postgres <c>xmin</c>).</summary>
    public uint Version { get; }

    public bool IsShared => ProductId is null;

    public static DomainResult<KbArticle> Create(
        Guid? productId,
        Guid? categoryId,
        string? slug,
        string? title,
        string? summary,
        string? bodyMarkdown,
        Guid authorId,
        TimeProvider clock)
    {
        var articleSlug = Guard.Slug(slug, DomainLimits.KbSlugMaxLength, "slug");
        var articleTitle = Guard.RequiredText(title, DomainLimits.KbTitleMaxLength, "title");
        var articleSummary = Guard.OptionalText(summary, DomainLimits.KbSummaryMaxLength, "summary");
        var body = Guard.RequiredText(bodyMarkdown, DomainLimits.KbBodyMaxLength, "body");
        if (Guard.FirstError(articleSlug, articleTitle, articleSummary, body) is { } error)
        {
            return error;
        }

        var now = DomainTime.Now(clock);
        return DomainResult<KbArticle>.Ok(new KbArticle(
            EntityId.New(clock), productId, categoryId, articleSlug.Value, articleTitle.Value, articleSummary.Value, body.Value,
            KbArticleStatus.Draft, authorId, now, now, null, 0));
    }

    public static KbArticle Restore(
        Guid id,
        Guid? productId,
        Guid? categoryId,
        string slug,
        string title,
        string? summary,
        string bodyMarkdown,
        KbArticleStatus status,
        Guid authorId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? publishedAt,
        uint version) =>
        new(id, productId, categoryId, slug, title, summary, bodyMarkdown, status, authorId, createdAt, updatedAt, publishedAt, version);

    public DomainResult Update(Guid? categoryId, string? title, string? summary, string? bodyMarkdown, TimeProvider clock)
    {
        var articleTitle = Guard.RequiredText(title, DomainLimits.KbTitleMaxLength, "title");
        var articleSummary = Guard.OptionalText(summary, DomainLimits.KbSummaryMaxLength, "summary");
        var body = Guard.RequiredText(bodyMarkdown, DomainLimits.KbBodyMaxLength, "body");
        if (Guard.FirstError(articleTitle, articleSummary, body) is { } error)
        {
            return error;
        }

        CategoryId = categoryId;
        Title = articleTitle.Value;
        Summary = articleSummary.Value;
        BodyMarkdown = body.Value;
        if (Status == KbArticleStatus.Archived)
        {
            // An edit reopens an archived article as a draft (D-044); the first publication date is kept.
            Status = KbArticleStatus.Draft;
        }

        UpdatedAt = DomainTime.Now(clock);
        return DomainResult.Ok();
    }

    public DomainResult Publish(TimeProvider clock)
    {
        if (Status == KbArticleStatus.Published)
        {
            return DomainErrors.Conflict("article-already-published", "The article is already published.");
        }

        if (FirstMissingForPublish() is { } missing)
        {
            return DomainErrors.Validation(PublishIncompleteCode, $"The article cannot be published without a {missing}.", missing);
        }

        var now = DomainTime.Now(clock);
        Status = KbArticleStatus.Published;
        PublishedAt ??= now;
        UpdatedAt = now;
        return DomainResult.Ok();
    }

    private string? FirstMissingForPublish()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return "title";
        }

        if (string.IsNullOrWhiteSpace(Slug))
        {
            return "slug";
        }

        if (string.IsNullOrWhiteSpace(BodyMarkdown))
        {
            return "body";
        }

        return CategoryId is null ? "category" : null;
    }

    public DomainResult Archive(TimeProvider clock)
    {
        if (Status == KbArticleStatus.Archived)
        {
            return DomainErrors.Conflict("article-already-archived", "The article is already archived.");
        }

        Status = KbArticleStatus.Archived;
        UpdatedAt = DomainTime.Now(clock);
        return DomainResult.Ok();
    }
}

/// <summary>Records that an agent reply (message) linked a knowledge-base article (FR-KB-08).</summary>
public sealed record TicketArticle(Guid TicketId, Guid MessageId, Guid ArticleId);
