using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Domain.Tests.Knowledge;

public sealed class KbArticleTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _author = Guid.NewGuid();

    private readonly Guid _category = Guid.NewGuid();

    private KbArticle Draft(Guid? productId = null, bool withCategory = true) =>
        KbArticle.Create(productId, withCategory ? _category : null, "reset-password", "Reset your password", "Short summary", "# Steps\n1. Click reset", _author, _clock).Value;

    [Fact]
    public void A_new_article_is_a_draft_with_no_publish_time()
    {
        var article = Draft();

        article.Status.ShouldBe(KbArticleStatus.Draft);
        article.PublishedAt.ShouldBeNull();
        article.CreatedAt.ShouldBe(_clock.GetUtcNow());
        article.UpdatedAt.ShouldBe(article.CreatedAt);
        article.AuthorId.ShouldBe(_author);
    }

    [Fact]
    public void An_article_without_a_product_is_shared()
    {
        Draft().IsShared.ShouldBeTrue();
        Draft(Guid.NewGuid()).IsShared.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Reset Password")]
    [InlineData("reset_password")]
    [InlineData("")]
    public void A_slug_must_be_lower_case_words_joined_by_hyphens(string slug)
    {
        KbArticle.Create(null, null, slug, "T", null, "b", _author, _clock).Error!.Code.ShouldBe("slug-invalid");
    }

    [Fact]
    public void Title_and_body_are_required_and_the_summary_is_optional()
    {
        KbArticle.Create(null, null, "a-b", " ", null, "b", _author, _clock).Error!.Code.ShouldBe("title-required");
        KbArticle.Create(null, null, "a-b", "T", null, " ", _author, _clock).Error!.Code.ShouldBe("body-required");
        KbArticle.Create(null, null, "a-b", "T", "  ", "b", _author, _clock).Value.Summary.ShouldBeNull();
    }

    [Fact]
    public void Publishing_a_draft_sets_the_publish_time()
    {
        var article = Draft();
        _clock.Advance(TimeSpan.FromHours(1));

        article.Publish(_clock).IsSuccess.ShouldBeTrue();

        article.Status.ShouldBe(KbArticleStatus.Published);
        article.PublishedAt.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public void Publishing_twice_is_a_conflict()
    {
        var article = Draft();
        article.Publish(_clock);

        article.Publish(_clock).Error!.Code.ShouldBe("article-already-published");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_draft_or_published_article_can_be_archived_once(bool publishFirst)
    {
        var article = Draft();
        if (publishFirst)
        {
            article.Publish(_clock);
        }

        article.Archive(_clock).IsSuccess.ShouldBeTrue();

        article.Status.ShouldBe(KbArticleStatus.Archived);
        article.Archive(_clock).Error!.Code.ShouldBe("article-already-archived");
    }

    [Fact]
    public void An_archived_article_can_be_published_again()
    {
        var article = Draft();
        article.Archive(_clock);

        article.Publish(_clock).IsSuccess.ShouldBeTrue();
        article.Status.ShouldBe(KbArticleStatus.Published);
    }

    [Fact]
    public void Editing_an_archived_article_returns_it_to_draft_and_keeps_the_first_publish_time()
    {
        var article = Draft();
        article.Publish(_clock);
        var firstPublished = article.PublishedAt;
        article.Archive(_clock);
        _clock.Advance(TimeSpan.FromHours(2));

        article.Update(_category, "New title", null, "new body", _clock).IsSuccess.ShouldBeTrue();

        article.Status.ShouldBe(KbArticleStatus.Draft);
        article.PublishedAt.ShouldBe(firstPublished);
        article.Title.ShouldBe("New title");
        article.UpdatedAt.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public void A_failed_edit_leaves_an_archived_article_archived()
    {
        var article = Draft();
        article.Archive(_clock);

        article.Update(_category, " ", null, "body", _clock).Error!.Code.ShouldBe("title-required");

        article.Status.ShouldBe(KbArticleStatus.Archived);
    }

    [Fact]
    public void Editing_a_published_article_keeps_it_published()
    {
        var article = Draft();
        article.Publish(_clock);

        article.Update(_category, "Live edit", null, "body", _clock).IsSuccess.ShouldBeTrue();

        article.Status.ShouldBe(KbArticleStatus.Published);
    }

    [Fact]
    public void Publishing_without_a_category_is_a_validation_error_on_category_and_changes_nothing()
    {
        var article = Draft(withCategory: false);

        var result = article.Publish(_clock);

        result.Error!.ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(DomainErrorKind.Validation),
            error => error.Code.ShouldBe("kb-publish-incomplete"),
            error => error.Target.ShouldBe("category"));
        article.Status.ShouldBe(KbArticleStatus.Draft);
        article.PublishedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("title")]
    [InlineData("slug")]
    [InlineData("body")]
    public void Publishing_a_restored_article_that_lacks_a_required_field_names_that_field(string missing)
    {
        var article = KbArticle.Restore(
            Guid.NewGuid(), null, _category, missing == "slug" ? "" : "a-slug", missing == "title" ? " " : "T", null, missing == "body" ? "" : "b",
            KbArticleStatus.Draft, _author, _clock.GetUtcNow(), _clock.GetUtcNow(), null, 1);

        var result = article.Publish(_clock);

        result.Error!.Code.ShouldBe("kb-publish-incomplete");
        result.Error.Target.ShouldBe(missing);
    }

    [Fact]
    public void Editing_changes_content_and_stamps_updated_at_but_never_the_slug()
    {
        var article = Draft();
        _clock.Advance(TimeSpan.FromMinutes(30));
        var category = Guid.NewGuid();

        article.Update(category, "Better title", "New summary", "new body", _clock).IsSuccess.ShouldBeTrue();

        article.Title.ShouldBe("Better title");
        article.CategoryId.ShouldBe(category);
        article.UpdatedAt.ShouldBe(_clock.GetUtcNow());
        article.Slug.ShouldBe("reset-password");
    }

    [Fact]
    public void A_category_validates_slug_and_name_and_can_be_shared()
    {
        var category = KbCategory.Create(null, "getting-started", "Getting started", 10, _clock).Value;

        category.IsShared.ShouldBeTrue();
        category.SortOrder.ShouldBe(10);
        KbCategory.Create(null, "Getting Started", "x", 0, _clock).Error!.Code.ShouldBe("slug-invalid");
        category.Update("Start here", "  Where to begin  ", 5).IsSuccess.ShouldBeTrue();
        category.Name.ShouldBe("Start here");
        category.Description.ShouldBe("Where to begin");
        category.Slug.ShouldBe("getting-started");
    }

    [Fact]
    public void The_category_slug_search_is_reserved_in_any_scope()
    {
        KbCategory.Create(null, "search", "Search", 0, _clock).Error!.ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("kb-category-reserved-slug"),
            error => error.Kind.ShouldBe(DomainErrorKind.Validation),
            error => error.Target.ShouldBe("slug"));
        KbCategory.Create(Guid.NewGuid(), "search", "Search", 0, _clock).Error!.Code.ShouldBe("kb-category-reserved-slug");
        KbCategory.Create(null, "searching", "Searching", 0, _clock).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_category_description_is_optional_and_limited()
    {
        KbCategory.Create(null, "faq", "FAQ", 0, _clock).Value.Description.ShouldBeNull();
        KbCategory.Create(null, "faq", "FAQ", 0, _clock, "Common questions").Value.Description.ShouldBe("Common questions");
        KbCategory.Create(null, "faq", "FAQ", 0, _clock, new string('x', 301)).Error!.Code.ShouldBe("description-too-long");
        var category = KbCategory.Create(null, "faq", "FAQ", 0, _clock).Value;
        category.Update("FAQ", new string('x', 301), 0).Error!.Code.ShouldBe("description-too-long");
        category.Description.ShouldBeNull();
    }

    [Fact]
    public void A_ticket_article_link_is_a_value_of_ticket_message_and_article()
    {
        var link = new TicketArticle(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        link.ShouldBe(link with { });
    }

    [Fact]
    public void Republishing_an_archived_article_keeps_the_first_publish_time()
    {
        var article = Draft();
        article.Publish(_clock).IsSuccess.ShouldBeTrue();
        var firstPublished = article.PublishedAt;
        _clock.Advance(TimeSpan.FromDays(1));
        article.Archive(_clock).IsSuccess.ShouldBeTrue();
        _clock.Advance(TimeSpan.FromDays(1));

        article.Publish(_clock).IsSuccess.ShouldBeTrue();

        article.PublishedAt.ShouldBe(firstPublished);
        article.UpdatedAt.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public void Stored_times_are_whole_microseconds()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero).AddTicks(3));
        var article = KbArticle.Create(null, _category, "a-slug", "T", null, "b", _author, clock).Value;
        (article.CreatedAt.Ticks % 10).ShouldBe(0);
        (article.UpdatedAt.Ticks % 10).ShouldBe(0);

        article.Publish(clock).IsSuccess.ShouldBeTrue();
        (article.PublishedAt!.Value.Ticks % 10).ShouldBe(0);
        (article.UpdatedAt.Ticks % 10).ShouldBe(0);

        article.Archive(clock).IsSuccess.ShouldBeTrue();
        (article.UpdatedAt.Ticks % 10).ShouldBe(0);
    }
}
