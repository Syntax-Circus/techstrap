using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The categories page (PHASE-08 T19): any agent lists, creates and edits (the sort order is saved through the update, with the version the list was read with); only an admin sees Delete, and a delete is blocked
/// while articles are in the category. A write whose outcome is unknown is held until the list is read again.
/// </summary>
public sealed class KbCategoriesPageTests : AdminPageTest
{
    private static readonly Guid PaperplaneId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid ShippingId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();

    public KbCategoriesPageTests()
    {
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<KbCategoryDto>>(
        [
            TestData.KbCategory("Account", "account", TestData.OrbitlyId, TestData.AccountCategoryId, sortOrder: 20, version: 4, description: "Sign-in and passwords"),
            TestData.KbCategory("Getting started", "getting-started", null, TestData.GettingStartedId, sortOrder: 10, version: 2),
            TestData.KbCategory("Shipping", "shipping", PaperplaneId, ShippingId, sortOrder: 30, version: 1),
        ]));
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product("Orbitly"), TestData.Product("Paperplane", PaperplaneId)]));
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<CreateKbCategoryRequest>();
            return TestData.Ok(TestData.KbCategory(request.Name!, request.Slug!, request.ProductId, Guid.NewGuid(), request.SortOrder, 1, request.Description));
        });
        _kb.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<UpdateKbCategoryRequest>();
            return TestData.Ok(TestData.KbCategory(request.Name!, "account", TestData.OrbitlyId, call.Arg<Guid>(), request.SortOrder, request.Version + 1, request.Description));
        });
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        Services.AddSingleton(_kb);
        Services.AddSingleton(_products);
    }

    private IRenderedComponent<KbCategoriesPage> RenderPage() => Render<KbCategoriesPage>();

    private IEnumerable<CreateKbCategoryRequest> Creates() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.CreateCategoryAsync)).Select(c => (CreateKbCategoryRequest)c.GetArguments()[0]!);

    private IEnumerable<(Guid Id, UpdateKbCategoryRequest Request)> Updates() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.UpdateCategoryAsync)).Select(c => ((Guid)c.GetArguments()[0]!, (UpdateKbCategoryRequest)c.GetArguments()[1]!));

    private IReadOnlyList<Guid> Deletes() =>
        [.. _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.DeleteCategoryAsync)).Select(c => (Guid)c.GetArguments()[0]!)];

    private static string Value(IRenderedComponent<KbCategoriesPage> cut, string id) => cut.Find($"#{id}").GetAttribute("value")!;

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<KbCategoriesPage> cut, string slug) => cut.Find($"tr[data-category='{slug}']");

    private static AngleSharp.Dom.IElement Dialog(IRenderedComponent<KbCategoriesPage> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.StartsWith("Delete the category ", StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<KbCategoriesPage> cut) => Dialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    // The first text of the name cell (the description, when there is one, sits in its own element after it).
    private static IReadOnlyList<string> RowNames(IRenderedComponent<KbCategoriesPage> cut) =>
        [.. cut.FindAll("tbody tr").Select(r => r.Children[0].ChildNodes.First(n => n.NodeType == AngleSharp.Dom.NodeType.Text && !string.IsNullOrWhiteSpace(n.TextContent)).TextContent.Trim())];

    // ---- the list ------------------------------------------------------------------------------------------------

    [Fact]
    public void Categories_are_listed_in_sort_order_with_their_product_slug_order_and_description()
    {
        var cut = RenderPage();

        cut.FindAll("tbody tr").Select(r => r.GetAttribute("data-category")).ShouldBe(["getting-started", "account", "shipping"]);
        Row(cut, "getting-started").Children[1].TextContent.ShouldBe("Shared");
        Row(cut, "account").Children[1].TextContent.ShouldBe("Orbitly");
        Row(cut, "shipping").Children[1].TextContent.ShouldBe("Paperplane");
        Row(cut, "account").QuerySelector("code")!.TextContent.ShouldBe("account");
        Row(cut, "account").QuerySelector(".ts-kb-category-order")!.TextContent.ShouldBe("20");
        Row(cut, "account").QuerySelector(".ts-kb-category-description")!.TextContent.ShouldBe("Sign-in and passwords");
        Row(cut, "shipping").QuerySelectorAll(".ts-kb-category-description").ShouldBeEmpty();
        cut.Find("div.ts-scroll").GetAttribute("aria-label").ShouldBe("Categories");
        _kb.Received(1).ListCategoriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_category_of_a_product_the_lookup_did_not_return_shows_a_fixed_phrase_and_never_its_id()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product("Orbitly")]));

        var cut = RenderPage();

        Row(cut, "shipping").Children[1].TextContent.ShouldBe("Another product");
        cut.Markup.ShouldNotContain(PaperplaneId.ToString());
    }

    [Fact]
    public void No_categories_is_a_plain_empty_state_and_the_create_form_is_still_there()
    {
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<KbCategoryDto>>([]));

        var cut = RenderPage();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No categories yet");
        cut.Find("form.ts-kb-category-create").ShouldNotBeNull();
        Value(cut, "ts-cat-order").ShouldBe("10");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<IReadOnlyList<KbCategoryDto>>("api-error", "The API is unavailable."),
            TestData.Ok<IReadOnlyList<KbCategoryDto>>([TestData.KbCategory()]));

        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the categories. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_read_that_throws_shows_the_load_error_with_fixed_copy_and_retry_loads_again()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromException<Result<IReadOnlyList<ProductDto>>>(new InvalidOperationException("boom at api.internal:5001")),
            _ => Task.FromResult(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product("Orbitly")])));

        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the categories. Try again in a moment.");
        cut.Markup.ShouldNotContain("boom");
        cut.Markup.ShouldNotContain("api.internal");
        cut.Find(".ts-state--error button").Click();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(3));
    }

    [Fact]
    public void A_load_that_was_overtaken_by_a_reload_never_replaces_the_newer_list()
    {
        var slow = new TaskCompletionSource<Result<IReadOnlyList<KbCategoryDto>>>();
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(
            _ => slow.Task,
            _ => Task.FromResult(TestData.Ok<IReadOnlyList<KbCategoryDto>>([TestData.KbCategory("Newer", "newer")])));
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderPage();
        cut.FindAll("tbody tr").ShouldBeEmpty();

        // A second read (the reload after an unknown outcome) starts while the first is still waiting.
        cut.Find("#ts-cat-name").Input("Billing");
        cut.Find("form.ts-kb-category-create").Submit();
        cut.WaitForAssertion(() => cut.Find(".ts-conflict button").ShouldNotBeNull());
        cut.Find(".ts-conflict button").Click();
        cut.WaitForAssertion(() => RowNames(cut).ShouldBe(["Newer"]));
        slow.SetResult(TestData.Ok<IReadOnlyList<KbCategoryDto>>([TestData.KbCategory("Older", "older")]));

        cut.WaitForAssertion(() => RowNames(cut).ShouldBe(["Newer"]));
    }

    // ---- who may do what -----------------------------------------------------------------------------------------

    [Fact]
    public void A_plain_agent_opens_the_page_lists_creates_and_edits_but_is_never_offered_delete()
    {
        AsAgent();

        var cut = RenderPage();

        cut.FindAll("tbody tr").Count.ShouldBe(3);
        cut.Find("form.ts-kb-category-create").ShouldNotBeNull();
        cut.FindAll("button.ts-edit").Count.ShouldBe(3);
        cut.FindAll("button.ts-delete").ShouldBeEmpty();
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();
    }

    [Fact]
    public void An_admin_is_offered_delete_on_every_row()
    {
        var cut = RenderPage();

        cut.FindAll("button.ts-delete").Count.ShouldBe(3);
    }

    // ---- create --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_new_category_starts_with_the_next_sort_order_after_the_highest_and_a_slug_that_follows_the_name()
    {
        var cut = RenderPage();

        Value(cut, "ts-cat-order").ShouldBe("40");
        cut.Find("#ts-cat-name").Input("Billing issues");
        Value(cut, "ts-cat-slug").ShouldBe("billing-issues");

        cut.Find("#ts-cat-slug").Input("billing");
        cut.Find("#ts-cat-name").Input("Billing and invoices");
        Value(cut, "ts-cat-slug").ShouldBe("billing");
        cut.FindAll("#ts-cat-product option").Select(o => o.TextContent).ShouldBe(["Shared by every product", "Orbitly", "Paperplane"]);
    }

    [Fact]
    public void Creating_sends_the_trimmed_values_and_never_cancels_adds_the_category_in_order_and_says_so()
    {
        var cut = RenderPage();
        cut.Find("#ts-cat-product").Change(PaperplaneId.ToString());
        cut.Find("#ts-cat-name").Input("  Billing  ");
        cut.Find("#ts-cat-description").Input("  Invoices and refunds ");
        cut.Find("#ts-cat-order").Input("15");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => Creates().Count().ShouldBe(1));
        Creates().Single().ShouldBe(new CreateKbCategoryRequest(PaperplaneId, "billing", "Billing", "Invoices and refunds", 15));
        ((CancellationToken)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.CreateCategoryAsync)).GetArguments()[1]!).CanBeCanceled.ShouldBeFalse();
        RowNames(cut).ShouldBe(["Getting started", "Billing", "Account", "Shipping"]);
        StatusMessages.Current.ShouldBe("Created the category Billing");
        Value(cut, "ts-cat-name").ShouldBe(string.Empty);
        Value(cut, "ts-cat-order").ShouldBe("40");
    }

    [Fact]
    public void A_blank_description_is_sent_as_nothing()
    {
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => Creates().Single().Description.ShouldBeNull());
    }

    [Fact]
    public void An_empty_form_sends_nothing_and_says_what_is_wrong()
    {
        var cut = RenderPage();
        cut.Find("#ts-cat-order").Input("many");

        cut.Find("form.ts-kb-category-create").Submit();

        Creates().ShouldBeEmpty();
        cut.FindAll("[role=alert]").Select(a => a.TextContent.Trim()).ShouldBe(["Enter a name.", "Enter a slug.", "Enter a whole number."]);
    }

    [Fact]
    public void The_slug_search_is_refused_before_it_is_sent_because_the_portal_keeps_it_for_its_search_page()
    {
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Search");

        cut.Find("form.ts-kb-category-create").Submit();

        Creates().ShouldBeEmpty();
        cut.Find("#ts-cat-slug-error").TextContent.ShouldBe("The slug \"search\" is kept for the portal's search page. Pick another.");
    }

    [Theory]
    [InlineData(ApiErrorCodes.KbCategorySlugTaken, "Another category already uses this slug. Slugs are shared across products, so pick another.")]
    [InlineData(ApiErrorCodes.KbCategoryReservedSlug, "The slug \"search\" is kept for the portal's search page. Pick another.")]
    public void A_slug_the_api_refuses_is_a_field_error_and_the_form_is_kept_for_another_try(string code, string message)
    {
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbCategoryDto>(code, "no", ResultErrorKind.Conflict));
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => cut.Find("#ts-cat-slug-error").TextContent.ShouldBe(message));
        Value(cut, "ts-cat-name").ShouldBe("Billing");
        RowNames(cut).Count.ShouldBe(3);
    }

    [Fact]
    public void A_400_names_its_field_and_a_message_for_no_field_is_shown_above_the_form()
    {
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(Result<KbCategoryDto>.Failure(
            new ResultError("description-too-long", "Too long.", ResultErrorKind.Validation, ApiFields.Description),
            [new ResultError("product-not-found", "No such product.", ResultErrorKind.Validation, "productId")]));
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => cut.Find("#ts-cat-description-error").TextContent.ShouldBe("Too long."));
        cut.Find("p.ts-form-error").TextContent.ShouldBe("No such product.");
    }

    [Fact]
    public void A_create_with_an_unknown_outcome_says_so_and_offers_a_reload()
    {
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.ApiUnavailable, "down"));
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict p").TextContent.ShouldBe("The change may have gone through. Reload the list to check before you try again."));
        cut.Find(".ts-conflict button").TextContent.ShouldBe("Reload list");
        Value(cut, "ts-cat-name").ShouldBe("Billing");
    }

    // ---- edit and re-order ---------------------------------------------------------------------------------------

    [Fact]
    public void Editing_opens_the_name_description_and_order_with_the_slug_and_product_fixed()
    {
        var cut = RenderPage();

        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();

        var row = Row(cut, "account");
        row.QuerySelector("#ts-cat-edit-name")!.GetAttribute("value").ShouldBe("Account");
        row.QuerySelector("#ts-cat-edit-description")!.GetAttribute("value").ShouldBe("Sign-in and passwords");
        row.QuerySelector("#ts-cat-edit-order")!.GetAttribute("value").ShouldBe("20");
        row.QuerySelector("code")!.TextContent.ShouldBe("account");
        row.Children[1].TextContent.ShouldBe("Orbitly");
        row.QuerySelectorAll("input").Count.ShouldBe(3);
    }

    // PHASE-08 T19: the sort order is persisted through the update call, with the version the list was read with.
    [Fact]
    public void Saving_a_new_sort_order_sends_the_update_with_the_loaded_version_and_re_sorts_the_list()
    {
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-order").Input("5");

        cut.Find("button.ts-save").Click();

        cut.WaitForAssertion(() => Updates().Count().ShouldBe(1));
        var (id, request) = Updates().Single();
        id.ShouldBe(TestData.AccountCategoryId);
        request.ShouldBe(new UpdateKbCategoryRequest("Account", "Sign-in and passwords", 5, Version: 4));
        ((CancellationToken)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.UpdateCategoryAsync)).GetArguments()[2]!).CanBeCanceled.ShouldBeFalse();
        cut.WaitForAssertion(() => RowNames(cut).ShouldBe(["Account", "Getting started", "Shipping"]));
        cut.FindAll("tr.ts-kb-category-editing").ShouldBeEmpty();
        StatusMessages.Current.ShouldBe("Saved the category Account");

        // The version in the answer is the one the next save sends.
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-order").Input("6");
        cut.Find("button.ts-save").Click();
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(2));
        Updates().Last().Request.Version.ShouldBe(5u);
    }

    [Fact]
    public void An_edit_that_fails_the_form_checks_sends_nothing()
    {
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-name").Input("  ");
        cut.Find("#ts-cat-edit-order").Input("x");

        cut.Find("button.ts-save").Click();

        Updates().ShouldBeEmpty();
        cut.Find("#ts-cat-edit-name-error").TextContent.ShouldBe("Enter a name.");
        cut.Find("#ts-cat-edit-order-error").TextContent.ShouldBe("Enter a whole number.");
    }

    [Fact]
    public void Cancelling_an_edit_changes_nothing()
    {
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-name").Input("Changed");

        cut.Find("button.ts-cancel").Click();

        Updates().ShouldBeEmpty();
        RowNames(cut)[1].ShouldStartWith("Account");
    }

    // Review Focus 4 (categories): a stale version is a 409 and never an overwrite.
    [Fact]
    public void A_409_says_the_category_changed_keeps_the_edit_open_and_offers_a_reload()
    {
        _kb.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateKbCategoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.ConcurrencyConflict, "changed", ResultErrorKind.Conflict));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-name").Input("My rename");

        cut.Find("button.ts-save").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict p").TextContent.ShouldBe("This category changed since you opened the list. Reload the list, then make your change again."));
        cut.Find("#ts-cat-edit-name").GetAttribute("value").ShouldBe("My rename");
        cut.Find(".ts-conflict button").Click();
        cut.WaitForAssertion(() => cut.FindAll(".ts-conflict").ShouldBeEmpty());
        _kb.Received(2).ListCategoriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void An_edit_of_a_category_deleted_meanwhile_says_so_and_reads_the_list_again()
    {
        _kb.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateKbCategoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.KbCategoryNotFound, "gone", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();

        cut.Find("button.ts-save").Click();

        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("That category no longer exists."));
        cut.WaitForAssertion(() => _kb.Received(2).ListCategoriesAsync(Arg.Any<CancellationToken>()));
        cut.FindAll("tr.ts-kb-category-editing").ShouldBeEmpty();
    }

    [Fact]
    public void An_edit_with_an_unknown_outcome_says_so_and_offers_a_reload()
    {
        _kb.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();

        cut.Find("button.ts-save").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict p").TextContent.ShouldContain("may have gone through"));
        cut.Find(".ts-conflict button").TextContent.ShouldBe("Reload list");
    }

    // ---- delete --------------------------------------------------------------------------------------------------

    [Fact]
    public void Delete_asks_first_naming_the_category_and_sends_nothing_until_it_is_confirmed()
    {
        var cut = RenderPage();

        Row(cut, "shipping").QuerySelector("button.ts-delete")!.Click();

        Dialog(cut).QuerySelector("h2")!.TextContent.ShouldBe("Delete the category Shipping?");
        Dialog(cut).TextContent.ShouldContain("This can't be undone.");
        Dialog(cut).ClassName!.ShouldContain("ts-dialog--danger");
        Dialogs.VerifyInvoke("open", 1);
        Deletes().ShouldBeEmpty();
    }

    [Fact]
    public void Confirming_deletes_once_and_never_cancels_and_removes_the_row()
    {
        var cut = RenderPage();
        Row(cut, "shipping").QuerySelector("button.ts-delete")!.Click();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => Deletes().ShouldBe([ShippingId]));
        ((CancellationToken)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.DeleteCategoryAsync)).GetArguments()[1]!).CanBeCanceled.ShouldBeFalse();
        cut.WaitForAssertion(() => RowNames(cut).ShouldBe(["Getting started", "Account"]));
        StatusMessages.Current.ShouldBe("Deleted the category Shipping");
    }

    [Fact]
    public void Cancelling_the_dialog_sends_nothing()
    {
        var cut = RenderPage();
        Row(cut, "shipping").QuerySelector("button.ts-delete")!.Click();

        Dialog(cut).QuerySelector(".ts-dialog-actions button.btn-outline-secondary")!.Click();

        Deletes().ShouldBeEmpty();
        RowNames(cut).Count.ShouldBe(3);
    }

    [Fact]
    public void A_category_with_articles_is_blocked_with_a_message_and_the_dialog_cannot_be_confirmed_again()
    {
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(ApiErrorCodes.KbCategoryInUse, "Move the 3 articles first.", ResultErrorKind.Conflict));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => Dialog(cut).QuerySelector(".ts-dialog-error")!.TextContent
            .ShouldBe("This category still has articles, so it can't be deleted. Move them to another category first. Move the 3 articles first."));
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        RowNames(cut).Count.ShouldBe(3);
        Confirm(cut).Click();
        Deletes().Count.ShouldBe(1);
    }

    [Fact]
    public void A_category_deleted_meanwhile_closes_the_dialog_and_reads_the_list_again()
    {
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.KbCategoryNotFound, "gone", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("That category no longer exists."));
        cut.WaitForAssertion(() => _kb.Received(2).ListCategoriesAsync(Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void A_delete_with_an_unknown_outcome_is_held_until_the_list_is_read_again()
    {
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();
        Confirm(cut).Click();

        cut.WaitForAssertion(() => Dialog(cut).QuerySelector(".ts-dialog-error")!.TextContent.ShouldBe("The delete may have gone through. Reload the list to check before you try again."));
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();

        // Asking again shows the same held state and sends nothing.
        Dialog(cut).QuerySelector(".ts-dialog-actions button.btn-outline-secondary")!.Click();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();
        Dialog(cut).QuerySelector(".ts-dialog-error")!.TextContent.ShouldContain("may have gone through");
        Deletes().Count.ShouldBe(1);

        // A later read releases it.
        Dialog(cut).QuerySelector("button.btn-link")!.Click();
        cut.WaitForAssertion(() => _kb.Received(2).ListCategoriesAsync(Arg.Any<CancellationToken>()));
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();
        Dialog(cut).QuerySelectorAll(".ts-dialog-error").ShouldBeEmpty();
        Confirm(cut).HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void A_403_says_only_an_admin_can_delete_and_cannot_be_confirmed_again()
    {
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.AdminAccessRequired, "no", ResultErrorKind.Forbidden));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => Dialog(cut).QuerySelector(".ts-dialog-error")!.TextContent.ShouldBe("Only an admin can delete a category."));
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task A_save_that_finishes_after_the_page_is_gone_changes_nothing_and_says_nothing()
    {
        var pending = new TaskCompletionSource<Result<KbCategoryDto>>();
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");
        var saving = Task.Run(() => cut.Find("form.ts-kb-category-create").Submit(), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => Creates().Count().ShouldBe(1));

        cut.Instance.Dispose();
        pending.SetResult(TestData.Ok(TestData.KbCategory("Billing", "billing")));
        await saving;
        await cut.InvokeAsync(() => { });

        StatusMessages.Current.ShouldBeNull();
    }
}

/// <summary>The checks the category form makes with the server's rules.</summary>
public sealed class KbCategoryFormTests
{
    [Theory]
    [InlineData("", "Enter a name.")]
    [InlineData("   ", "Enter a name.")]
    public void A_blank_name_is_refused(string value, string message) => KbCategoryForm.CheckName(value).ShouldBe(message);

    [Fact]
    public void The_name_limit_is_100_and_the_description_limit_300()
    {
        KbCategoryForm.CheckName(new string('a', 100)).ShouldBeNull();
        KbCategoryForm.CheckName(new string('a', 101)).ShouldBe("Use 100 characters or fewer.");
        KbCategoryForm.CheckDescription(new string('a', 300)).ShouldBeNull();
        KbCategoryForm.CheckDescription(new string('a', 301)).ShouldBe("Use 300 characters or fewer.");
        KbCategoryForm.CheckDescription(string.Empty).ShouldBeNull();
    }

    [Theory]
    [InlineData("getting-started", null)]
    [InlineData("a", null)]
    [InlineData("", "Enter a slug.")]
    [InlineData("Getting-started", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData("getting--started", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData("-getting", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData("search", "The slug \"search\" is kept for the portal's search page. Pick another.")]
    public void A_slug_is_lower_case_words_and_never_the_reserved_search(string value, string? message) => KbCategoryForm.CheckSlug(value).ShouldBe(message);

    [Theory]
    [InlineData("10", true, 10)]
    [InlineData(" -5 ", true, -5)]
    [InlineData("0", true, 0)]
    [InlineData("1.5", false, 0)]
    [InlineData("many", false, 0)]
    [InlineData("", false, 0)]
    public void The_sort_order_is_a_whole_number(string value, bool valid, int expected)
    {
        KbCategoryForm.TryParseSortOrder(value, out var parsed).ShouldBe(valid);
        parsed.ShouldBe(expected);
        (KbCategoryForm.CheckSortOrder(value) is null).ShouldBe(valid);
    }

    [Fact]
    public void The_next_sort_order_is_one_step_after_the_highest_and_an_empty_list_starts_at_the_step()
    {
        KbCategoryForm.NextSortOrder([]).ShouldBe(10);
        KbCategoryForm.NextSortOrder(
        [
            KbCategoryRowViewModel.From(TestData.KbCategory(sortOrder: 20), []),
            KbCategoryRowViewModel.From(TestData.KbCategory(sortOrder: 35), []),
        ]).ShouldBe(45);
    }

    [Theory]
    [InlineData("Getting started", "getting-started")]
    [InlineData("  Billing & invoices!  ", "billing-invoices")]
    public void A_slug_is_suggested_from_the_name(string name, string expected) => KbCategoryForm.SlugFrom(name).ShouldBe(expected);

    [Fact]
    public void Rows_are_ordered_by_sort_order_then_name_then_slug()
    {
        var rows = KbCategoryRowViewModel.Sorted(
        [
            KbCategoryRowViewModel.From(TestData.KbCategory("b", "b", sortOrder: 5), []),
            KbCategoryRowViewModel.From(TestData.KbCategory("A", "a2", sortOrder: 5), []),
            KbCategoryRowViewModel.From(TestData.KbCategory("a", "a1", sortOrder: 5), []),
            KbCategoryRowViewModel.From(TestData.KbCategory("z", "z", sortOrder: 1), []),
        ]);

        rows.Select(r => r.Slug).ShouldBe(["z", "a1", "a2", "b"]);
    }
}
