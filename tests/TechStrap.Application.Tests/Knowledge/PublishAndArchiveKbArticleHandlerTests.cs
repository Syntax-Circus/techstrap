using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class PublishAndArchiveKbArticleHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private PublishKbArticleRequestHandler Publisher(params Result[] commits) => new(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, _kb.Renderer, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    private ArchiveKbArticleRequestHandler Archiver(params Result[] commits) => new(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    [Fact]
    public async Task Publishing_a_complete_draft_sets_the_publish_time_and_stages_it()
    {
        var article = _kb.StoredArticle(categoryId: Guid.NewGuid());
        _kb.Clock.Advance(TimeSpan.FromHours(1));

        var result = await Publisher().HandleAsync(article.Id, null, Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Published),
            dto => dto.PublishedAt.ShouldBe(_kb.Clock.GetUtcNow()));
        _kb.KnowledgeBase.Received(1).UpdateArticle(article);
    }

    [Fact]
    public async Task Publishing_without_a_category_is_a_validation_error_on_category_and_nothing_is_staged()
    {
        var article = _kb.StoredArticle(categoryId: null);

        var result = await Publisher().HandleAsync(article.Id, null, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-publish-incomplete"),
            error => error.Target.ShouldBe("category"));
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Status.ShouldBe(KbArticleStatus.Draft);
    }

    [Fact]
    public async Task Publishing_a_published_article_is_a_conflict()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Published, categoryId: Guid.NewGuid(), publishedAt: _kb.Clock.GetUtcNow());

        var result = await Publisher().HandleAsync(article.Id, null, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("article-already-published"));
    }

    [Fact]
    public async Task Republishing_an_archived_article_keeps_the_first_publish_time()
    {
        var first = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var article = _kb.StoredArticle(KbArticleStatus.Archived, categoryId: Guid.NewGuid(), publishedAt: first);

        var result = await Publisher().HandleAsync(article.Id, null, Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Published),
            dto => dto.PublishedAt.ShouldBe(first));
    }

    [Fact]
    public async Task A_stale_version_stops_a_publish_and_a_matching_one_goes_through()
    {
        var article = _kb.StoredArticle(categoryId: Guid.NewGuid());

        var stale = await Publisher().HandleAsync(article.Id, 4, Ct);
        var current = await Publisher().HandleAsync(article.Id, 5, Ct);

        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        current.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Publish_and_archive_of_an_unknown_article_are_not_found()
    {
        (await Publisher().HandleAsync(Guid.NewGuid(), null, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
        (await Archiver().HandleAsync(Guid.NewGuid(), null, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
    }

    [Fact]
    public async Task Archiving_a_published_article_archives_it_and_keeps_the_publish_time()
    {
        var published = _kb.Clock.GetUtcNow();
        var article = _kb.StoredArticle(KbArticleStatus.Published, categoryId: Guid.NewGuid(), publishedAt: published);
        _kb.Clock.Advance(TimeSpan.FromHours(2));

        var result = await Archiver().HandleAsync(article.Id, null, Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Archived),
            dto => dto.PublishedAt.ShouldBe(published),
            dto => dto.UpdatedAt.ShouldBe(_kb.Clock.GetUtcNow()));
        _kb.KnowledgeBase.Received(1).UpdateArticle(article);
    }

    [Fact]
    public async Task Archiving_an_archived_article_is_a_conflict_and_a_stale_version_stops_an_archive()
    {
        var archived = _kb.StoredArticle(KbArticleStatus.Archived);
        var draft = _kb.StoredArticle();

        var twice = await Archiver().HandleAsync(archived.Id, null, Ct);
        var stale = await Archiver().HandleAsync(draft.Id, 1, Ct);

        twice.Errors.ShouldHaveSingleItem().Code.ShouldBe("article-already-archived");
        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(draft);
    }

    [Fact]
    public async Task A_conflict_found_at_commit_is_passed_through()
    {
        var article = _kb.StoredArticle(categoryId: Guid.NewGuid());

        var result = await Publisher(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict)).HandleAsync(article.Id, null, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task A_deactivated_agent_cannot_publish_or_archive_and_nothing_is_loaded_or_changed()
    {
        var draft = _kb.StoredArticle(categoryId: Guid.NewGuid());
        var published = _kb.StoredArticle(KbArticleStatus.Published, categoryId: Guid.NewGuid(), publishedAt: _kb.Clock.GetUtcNow());
        _kb.Agent.SetActive(false);

        var publish = await Publisher().HandleAsync(draft.Id, null, Ct);
        var archive = await Archiver().HandleAsync(published.Id, null, Ct);

        publish.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        archive.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().GetArticleAsync(default, Ct);
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        draft.Status.ShouldBe(KbArticleStatus.Draft);
        published.Status.ShouldBe(KbArticleStatus.Published);
    }

    [Fact]
    public async Task Publishing_a_body_over_the_element_cap_is_refused_on_body_and_the_article_is_unchanged()
    {
        var article = _kb.StoredArticle(categoryId: Guid.NewGuid());
        _kb.Renderer.IsTooComplex("Steps.").Returns(true);

        var result = await Publisher().HandleAsync(article.Id, null, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-body-too-complex"),
            error => error.Target.ShouldBe("body"));
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Status.ShouldBe(KbArticleStatus.Draft);
        article.PublishedAt.ShouldBeNull();
    }
}
