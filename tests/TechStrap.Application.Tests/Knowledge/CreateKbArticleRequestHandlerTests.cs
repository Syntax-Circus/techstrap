using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class CreateKbArticleRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private CreateKbArticleRequestHandler Handler(params Result[] commits) =>
        new(_kb.Claims, _kb.Agents, _kb.Products, _kb.KnowledgeBase, _kb.Renderer, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    private static CreateKbArticleRequest Request(Guid? productId = null, Guid? categoryId = null, string? slug = "reset-password", string? title = "Reset your password", string? body = "Steps.") =>
        new(productId, categoryId, slug, title, "Short", body);

    [Fact]
    public async Task A_new_article_is_a_draft_by_the_signed_in_agent_and_the_dto_carries_the_stored_version()
    {
        var category = _kb.StoredCategory(null);
        _kb.KnowledgeBase.GetArticleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var created = (KbArticle)_kb.KnowledgeBase.ReceivedCalls().First(c => c.GetMethodInfo().Name == nameof(IKbRepository.AddArticle)).GetArguments()[0]!;
            return KbArticle.Restore(created.Id, created.ProductId, created.CategoryId, created.Slug, created.Title, created.Summary, created.BodyMarkdown, created.Status, created.AuthorId, created.CreatedAt, created.UpdatedAt, null, 9);
        });

        var result = await Handler().HandleAsync(Request(_kb.Orbitly.Id, category.Id), Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Draft),
            dto => dto.ProductId.ShouldBe(_kb.Orbitly.Id),
            dto => dto.CategoryId.ShouldBe(category.Id),
            dto => dto.AuthorAgentId.ShouldBe(_kb.Agent.Id),
            dto => dto.Slug.ShouldBe("reset-password"),
            dto => dto.PublishedAt.ShouldBeNull(),
            dto => dto.Version.ShouldBe(9u));
        _kb.KnowledgeBase.Received(1).AddArticle(Arg.Is<KbArticle>(a => a.Status == KbArticleStatus.Draft && a.ProductId == _kb.Orbitly.Id));
    }

    [Fact]
    public async Task A_null_product_makes_a_shared_article()
    {
        var result = await Handler().HandleAsync(Request(), Ct);

        result.Value.ProductId.ShouldBeNull();
        _kb.KnowledgeBase.Received(1).AddArticle(Arg.Is<KbArticle>(a => a.IsShared));
    }

    [Fact]
    public async Task A_bad_slug_is_a_validation_error_on_slug_and_nothing_is_staged()
    {
        var result = await Handler().HandleAsync(Request(slug: "Not A Slug"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("slug"));
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task A_missing_title_is_a_validation_error_on_title()
    {
        var result = await Handler().HandleAsync(Request(title: " "), Ct);

        result.Errors.ShouldHaveSingleItem().Target.ShouldBe("title");
    }

    [Fact]
    public async Task An_unknown_product_is_a_validation_error_on_product_id()
    {
        var result = await Handler().HandleAsync(Request(Guid.NewGuid()), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("product-not-found"),
            error => error.Target.ShouldBe("productId"));
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task An_unknown_category_is_a_validation_error_on_category_id()
    {
        var result = await Handler().HandleAsync(Request(categoryId: Guid.NewGuid()), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-category-not-found"),
            error => error.Target.ShouldBe("categoryId"));
    }

    [Fact]
    public async Task A_shared_article_cannot_use_a_product_category()
    {
        var category = _kb.StoredCategory(_kb.Orbitly.Id);

        var result = await Handler().HandleAsync(Request(null, category.Id), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-scope-mismatch");
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task A_product_article_cannot_use_another_products_category()
    {
        var category = _kb.StoredCategory(_kb.Paperplane.Id);

        var result = await Handler().HandleAsync(Request(_kb.Orbitly.Id, category.Id), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-scope-mismatch");
    }

    [Fact]
    public async Task A_product_article_may_use_a_shared_category_or_one_of_its_own()
    {
        var shared = _kb.StoredCategory(null, "general");
        var own = _kb.StoredCategory(_kb.Orbitly.Id, "own");

        (await Handler().HandleAsync(Request(_kb.Orbitly.Id, shared.Id, "one"), Ct)).IsSuccess.ShouldBeTrue();
        (await Handler().HandleAsync(Request(_kb.Orbitly.Id, own.Id, "two"), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_slug_already_used_in_either_scope_is_a_conflict_and_nothing_is_staged()
    {
        _kb.KnowledgeBase.ArticleSlugTakenAsync(_kb.Orbitly.Id, "reset-password", Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().HandleAsync(Request(_kb.Orbitly.Id), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("kb-slug-taken"));
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task A_duplicate_found_at_commit_is_the_same_conflict()
    {
        var result = await Handler(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.Duplicate)).HandleAsync(Request(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-slug-taken");
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_and_nothing_is_staged()
    {
        _kb.Agent.SetActive(false);

        var result = await Handler().HandleAsync(Request(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task The_agent_check_runs_before_any_product_category_or_slug_lookup()
    {
        var category = _kb.StoredCategory(_kb.Orbitly.Id);
        _kb.Agent.SetActive(false);

        var result = await Handler().HandleAsync(Request(_kb.Orbitly.Id, category.Id), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        await _kb.Products.DidNotReceiveWithAnyArgs().GetByIdAsync(default, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().GetCategoryAsync(default, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ArticleSlugTakenAsync(default, default!, Ct);
    }

    [Fact]
    public async Task A_reference_violation_at_commit_is_not_a_slug_conflict_and_comes_back_unchanged()
    {
        // A product or category that was deleted between the checks and the commit: the persistence conflict is returned as it is, never relabelled as a taken slug.
        var result = await Handler(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ReferenceViolation)).HandleAsync(Request(), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe(PersistenceErrorCodes.ReferenceViolation),
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict));
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_repository_calls()
    {
        using var source = new CancellationTokenSource();
        var category = _kb.StoredCategory(null);

        await Handler().HandleAsync(Request(_kb.Orbitly.Id, category.Id), source.Token);

        await _kb.KnowledgeBase.Received().GetCategoryAsync(category.Id, source.Token);
        await _kb.KnowledgeBase.Received().ArticleSlugTakenAsync(_kb.Orbitly.Id, "reset-password", source.Token);
    }

    [Fact]
    public async Task A_body_over_the_element_cap_is_refused_on_body_and_nothing_is_staged_or_looked_up()
    {
        _kb.Renderer.IsTooComplex("Too many cells.").Returns(true);

        var result = await Handler().HandleAsync(Request(_kb.Orbitly.Id, body: "Too many cells."), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-body-too-complex"),
            error => error.Target.ShouldBe("body"));
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ArticleSlugTakenAsync(default, default!, Ct);
        await _kb.Products.DidNotReceiveWithAnyArgs().GetByIdAsync(default, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().GetCategoryAsync(default, Ct);
    }

    [Fact]
    public async Task An_empty_body_is_a_body_validation_error_and_the_renderer_is_not_asked()
    {
        _kb.Renderer.IsTooComplex(Arg.Any<string>()).Returns(true);

        var result = await Handler().HandleAsync(Request(body: string.Empty), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldNotBe("kb-body-too-complex"),
            error => error.Target.ShouldBe("body"));
        _kb.Renderer.DidNotReceiveWithAnyArgs().IsTooComplex(default!);
    }
}
