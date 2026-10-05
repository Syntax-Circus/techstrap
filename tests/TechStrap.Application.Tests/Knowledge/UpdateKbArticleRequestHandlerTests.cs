using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>Review Focus 4: an edit never overwrites a newer version, and an Archived article is edited back to Draft.</summary>
public sealed class UpdateKbArticleRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private UpdateKbArticleRequestHandler Handler(params Result[] commits) => new(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, _kb.Renderer, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    [Fact]
    public async Task An_edit_changes_the_content_and_stages_the_article()
    {
        var category = _kb.StoredCategory(null);
        var article = _kb.StoredArticle(KbArticleStatus.Draft, productId: _kb.Orbitly.Id);
        _kb.Clock.Advance(TimeSpan.FromHours(1));

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(category.Id, "New title", "New summary", "New body", 5), Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Title.ShouldBe("New title"),
            dto => dto.Summary.ShouldBe("New summary"),
            dto => dto.BodyMarkdown.ShouldBe("New body"),
            dto => dto.CategoryId.ShouldBe(category.Id),
            dto => dto.Slug.ShouldBe("reset-password"),
            dto => dto.UpdatedAt.ShouldBe(_kb.Clock.GetUtcNow()));
        _kb.KnowledgeBase.Received(1).UpdateArticle(article);
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict_and_nothing_is_saved()
    {
        var article = _kb.StoredArticle();

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "New title", null, "New body", 4), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Title.ShouldBe("Reset your password");
    }

    [Fact]
    public async Task A_conflict_found_at_commit_is_passed_through()
    {
        var article = _kb.StoredArticle();

        var result = await Handler(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict))
            .HandleAsync(article.Id, new UpdateKbArticleRequest(null, "New title", null, "New body", 5), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task An_archived_article_comes_back_as_a_draft_with_its_first_publish_time()
    {
        var published = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var article = _kb.StoredArticle(KbArticleStatus.Archived, categoryId: Guid.NewGuid(), publishedAt: published);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "Revised", null, "Revised body", 5), Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Draft),
            dto => dto.PublishedAt.ShouldBe(published));
        _kb.KnowledgeBase.Received(1).UpdateArticle(article);
    }

    [Fact]
    public async Task Editing_a_published_article_keeps_it_published()
    {
        var category = _kb.StoredCategory(null);
        var article = _kb.StoredArticle(KbArticleStatus.Published, categoryId: category.Id, publishedAt: _kb.Clock.GetUtcNow());

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(category.Id, "Live edit", null, "Live body", 5), Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Published),
            dto => dto.CategoryId.ShouldBe(category.Id),
            dto => dto.PublishedAt.ShouldBe(article.PublishedAt));
    }

    [Fact]
    public async Task An_unknown_article_is_not_found()
    {
        var result = await Handler().HandleAsync(Guid.NewGuid(), new UpdateKbArticleRequest(null, "T", null, "B", 1), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.NotFound),
            error => error.Code.ShouldBe("kb-article-not-found"));
    }

    [Fact]
    public async Task A_category_of_another_scope_is_refused_and_the_article_is_not_changed()
    {
        var paperplaneCategory = _kb.StoredCategory(_kb.Paperplane.Id);
        var article = _kb.StoredArticle(productId: _kb.Orbitly.Id);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(paperplaneCategory.Id, "New title", null, "New body", 5), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-scope-mismatch");
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Title.ShouldBe("Reset your password");
    }

    [Fact]
    public async Task A_blank_title_is_a_validation_error_and_an_archived_article_stays_archived()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Archived);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, " ", null, "Body", 5), Ct);

        result.Errors.ShouldHaveSingleItem().Target.ShouldBe("title");
        article.Status.ShouldBe(KbArticleStatus.Archived);
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_repository()
    {
        using var source = new CancellationTokenSource();
        var article = _kb.StoredArticle();
        _kb.KnowledgeBase.GetArticleAsync(article.Id, source.Token).Returns(article);

        await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "T", null, "B", 5), source.Token);

        await _kb.KnowledgeBase.Received().GetArticleAsync(article.Id, source.Token);
    }

    [Fact]
    public async Task A_body_over_the_element_cap_is_refused_on_body_and_the_article_is_not_changed_or_staged()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Archived);
        _kb.Renderer.IsTooComplex("Too many cells.").Returns(true);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "Revised", null, "Too many cells.", 5), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-body-too-complex"),
            error => error.Target.ShouldBe("body"));
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Title.ShouldBe("Reset your password");
        article.Status.ShouldBe(KbArticleStatus.Archived);
    }

    [Fact]
    public async Task An_empty_body_is_a_body_validation_error_and_the_renderer_is_not_asked()
    {
        var article = _kb.StoredArticle();
        _kb.Renderer.IsTooComplex(Arg.Any<string>()).Returns(true);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "T", null, string.Empty, 5), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldNotBe("kb-body-too-complex"),
            error => error.Target.ShouldBe("body"));
        _kb.Renderer.DidNotReceiveWithAnyArgs().IsTooComplex(default!);
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_and_nothing_is_loaded_or_changed()
    {
        var article = _kb.StoredArticle();
        _kb.Agent.SetActive(false);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "New title", null, "New body", 5), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().GetArticleAsync(default, Ct);
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Title.ShouldBe("Reset your password");
    }

    [Fact]
    public async Task An_unknown_agent_is_refused_and_nothing_is_loaded()
    {
        var article = _kb.StoredArticle();
        _kb.Agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns((TechStrap.Domain.Agents.Agent?)null);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "New title", null, "New body", 5), Ct);

        result.IsFailure.ShouldBeTrue();
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().GetArticleAsync(default, Ct);
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task A_stale_version_wins_over_a_too_complex_body()
    {
        var article = _kb.StoredArticle();
        _kb.Renderer.IsTooComplex(Arg.Any<string>()).Returns(true);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "T", null, "Too many cells.", 4), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task An_unknown_category_is_a_validation_error_on_category_id_and_nothing_is_changed()
    {
        var article = _kb.StoredArticle();

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(Guid.NewGuid(), "New title", null, "New body", 5), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-category-not-found"),
            error => error.Target.ShouldBe("categoryId"));
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Title.ShouldBe("Reset your password");
    }

    [Fact]
    public async Task A_published_article_cannot_lose_its_category_and_is_unchanged()
    {
        var categoryId = Guid.NewGuid();
        var article = _kb.StoredArticle(KbArticleStatus.Published, categoryId: categoryId, publishedAt: _kb.Clock.GetUtcNow());

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "New title", null, "New body", 5), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("kb-publish-incomplete"),
            error => error.Target.ShouldBe("category"));
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.CategoryId.ShouldBe(categoryId);
        article.Title.ShouldBe("Reset your password");
    }
}
