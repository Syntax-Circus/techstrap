using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class KbCategoryHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private CreateKbCategoryRequestHandler Creator(params Result[] commits) => new(_kb.Claims, _kb.Agents, _kb.Products, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    [Fact]
    public async Task A_category_is_created_with_its_description_and_the_stored_version()
    {
        _kb.KnowledgeBase.GetCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            KbCategory.Restore(call.Arg<Guid>(), _kb.Orbitly.Id, "Account", "account", "Sign-in help", 2, 11));

        var result = await Creator().HandleAsync(new CreateKbCategoryRequest(_kb.Orbitly.Id, "account", "Account", "Sign-in help", 2), Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Slug.ShouldBe("account"),
            dto => dto.Description.ShouldBe("Sign-in help"),
            dto => dto.SortOrder.ShouldBe(2),
            dto => dto.Version.ShouldBe(11u));
        _kb.KnowledgeBase.Received(1).AddCategory(Arg.Is<KbCategory>(c => c.Slug == "account" && c.ProductId == _kb.Orbitly.Id));
    }

    [Fact]
    public async Task The_search_slug_is_reserved_in_any_scope()
    {
        var result = await Creator().HandleAsync(new CreateKbCategoryRequest(null, "search", "Search", null, 0), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-category-reserved-slug"),
            error => error.Target.ShouldBe("slug"));
        _kb.KnowledgeBase.DidNotReceive().AddCategory(Arg.Any<KbCategory>());
    }

    [Fact]
    public async Task A_slug_used_in_either_scope_is_a_conflict_and_a_duplicate_at_commit_is_the_same_conflict()
    {
        _kb.KnowledgeBase.CategorySlugTakenAsync(null, "faq", Arg.Any<CancellationToken>()).Returns(true);

        var pre = await Creator().HandleAsync(new CreateKbCategoryRequest(null, "faq", "FAQ", null, 0), Ct);
        var race = await Creator(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.Duplicate)).HandleAsync(new CreateKbCategoryRequest(null, "other", "Other", null, 0), Ct);

        pre.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(error => error.Kind.ShouldBe(ResultErrorKind.Conflict), error => error.Code.ShouldBe("kb-category-slug-taken"));
        race.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-slug-taken");
        _kb.KnowledgeBase.DidNotReceive().AddCategory(Arg.Is<KbCategory>(c => c.Slug == "faq"));
    }

    [Fact]
    public async Task An_unknown_product_is_a_validation_error_on_product_id()
    {
        var result = await Creator().HandleAsync(new CreateKbCategoryRequest(Guid.NewGuid(), "faq", "FAQ", null, 0), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(error => error.Code.ShouldBe("product-not-found"), error => error.Target.ShouldBe("productId"));
    }

    [Fact]
    public async Task An_update_changes_name_description_and_sort_order()
    {
        var category = _kb.StoredCategory(null);
        var handler = new UpdateKbCategoryRequestHandler(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create());

        var result = await handler.HandleAsync(category.Id, new UpdateKbCategoryRequest("Sign in", "Help", 4, 3), Ct);

        result.Value.ShouldSatisfyAllConditions(dto => dto.Name.ShouldBe("Sign in"), dto => dto.Description.ShouldBe("Help"), dto => dto.SortOrder.ShouldBe(4), dto => dto.Slug.ShouldBe("account"));
        _kb.KnowledgeBase.Received(1).UpdateCategory(category);
    }

    [Fact]
    public async Task A_stale_category_version_is_a_conflict_and_an_unknown_category_is_not_found()
    {
        var category = _kb.StoredCategory(null);
        var handler = new UpdateKbCategoryRequestHandler(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create());

        var stale = await handler.HandleAsync(category.Id, new UpdateKbCategoryRequest("Sign in", null, 1, 2), Ct);
        var missing = await handler.HandleAsync(Guid.NewGuid(), new UpdateKbCategoryRequest("Sign in", null, 1, 1), Ct);

        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        missing.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
        _kb.KnowledgeBase.DidNotReceive().UpdateCategory(Arg.Any<KbCategory>());
    }

    [Fact]
    public async Task A_blank_name_is_a_validation_error_on_name()
    {
        var category = _kb.StoredCategory(null);
        var handler = new UpdateKbCategoryRequestHandler(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create());

        var result = await handler.HandleAsync(category.Id, new UpdateKbCategoryRequest(" ", null, 1, 3), Ct);

        result.Errors.ShouldHaveSingleItem().Target.ShouldBe("name");
    }

    [Fact]
    public async Task A_category_holding_articles_is_not_deleted()
    {
        var category = _kb.StoredCategory(null);
        _kb.KnowledgeBase.CategoryHasArticlesAsync(category.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new DeleteKbCategoryRequestHandler(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create()).HandleAsync(category.Id, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(error => error.Kind.ShouldBe(ResultErrorKind.Conflict), error => error.Code.ShouldBe("kb-category-in-use"));
        _kb.KnowledgeBase.DidNotReceive().RemoveCategory(Arg.Any<KbCategory>());
    }

    [Fact]
    public async Task An_empty_category_is_deleted_and_a_reference_violation_at_commit_is_the_same_conflict()
    {
        var category = _kb.StoredCategory(null);

        var deleted = await new DeleteKbCategoryRequestHandler(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create()).HandleAsync(category.Id, Ct);
        var raced = await new DeleteKbCategoryRequestHandler(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ReferenceViolation)))
            .HandleAsync(category.Id, Ct);

        deleted.IsSuccess.ShouldBeTrue();
        _kb.KnowledgeBase.Received(2).RemoveCategory(category);
        raced.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-in-use");
    }

    [Fact]
    public async Task Deleting_an_unknown_category_is_not_found()
    {
        var result = await new DeleteKbCategoryRequestHandler(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create()).HandleAsync(Guid.NewGuid(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
    }

    [Fact]
    public async Task Listing_forwards_the_product_and_shared_choice_and_maps_each_category()
    {
        var category = KbCategory.Restore(Guid.NewGuid(), null, "Account", "account", null, 1, 3);
        _kb.KnowledgeBase.ListCategoriesAsync(_kb.Orbitly.Id, false, Arg.Any<CancellationToken>()).Returns([category]);

        var result = await new ListKbCategoriesRequestHandler(_kb.KnowledgeBase).HandleAsync(_kb.Orbitly.Id, false, Ct);

        result.Value.ShouldHaveSingleItem().ShouldBe(new KbCategoryDto(category.Id, null, "account", "Account", null, 1, 3));
    }

    [Fact]
    public async Task A_deactivated_agent_cannot_create_update_or_delete_a_category_and_nothing_is_loaded_or_changed()
    {
        var category = _kb.StoredCategory(null);
        _kb.Agent.SetActive(false);

        var created = await Creator().HandleAsync(new CreateKbCategoryRequest(_kb.Orbitly.Id, "faq", "FAQ", null, 0), Ct);
        var updated = await new UpdateKbCategoryRequestHandler(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create()).HandleAsync(category.Id, new UpdateKbCategoryRequest("Sign in", null, 1, 3), Ct);
        var deleted = await new DeleteKbCategoryRequestHandler(_kb.Claims, _kb.Agents, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create()).HandleAsync(category.Id, Ct);

        created.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        updated.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        deleted.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        await _kb.Products.DidNotReceiveWithAnyArgs().GetByIdAsync(default, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().GetCategoryAsync(default, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().CategorySlugTakenAsync(default, default!, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().CategoryHasArticlesAsync(default, Ct);
        _kb.KnowledgeBase.DidNotReceive().AddCategory(Arg.Any<KbCategory>());
        _kb.KnowledgeBase.DidNotReceive().UpdateCategory(Arg.Any<KbCategory>());
        _kb.KnowledgeBase.DidNotReceive().RemoveCategory(Arg.Any<KbCategory>());
        category.Name.ShouldBe("Account");
    }
}
