using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Domain.Tests.Knowledge;

public sealed class KbArticleTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _author = Guid.NewGuid();

    private KbArticle Draft(Guid? productId = null) =>
        KbArticle.Create(productId, null, "reset-password", "Reset your password", "Short summary", "# Steps\n1. Click reset", _author, _clock).Value;

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
    public void An_archived_article_can_be_published_again_but_not_edited()
    {
        var article = Draft();
        article.Archive(_clock);

        article.Update(null, "New", null, "body", _clock).Error!.Code.ShouldBe("article-archived");
        article.Publish(_clock).IsSuccess.ShouldBeTrue();
        article.Status.ShouldBe(KbArticleStatus.Published);
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
        category.Update("Start here", 5).IsSuccess.ShouldBeTrue();
        category.Name.ShouldBe("Start here");
        category.Slug.ShouldBe("getting-started");
    }

    [Fact]
    public void A_ticket_article_link_is_a_value_of_ticket_message_and_article()
    {
        var link = new TicketArticle(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        link.ShouldBe(link with { });
    }
}
