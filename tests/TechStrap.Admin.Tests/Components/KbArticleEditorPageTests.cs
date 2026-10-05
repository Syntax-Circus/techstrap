using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Options;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The article editor (PHASE-08 T17): create and edit, save with the loaded version, a 409 that keeps the draft, publish and archive, the leave guard and "View on portal". Review Focus 4: a lost edit on a
/// version conflict, and an archived article that becomes a draft again when it is edited.
/// </summary>
public sealed class KbArticleEditorPageTests : AdminComponentTest
{
    private const string LeaveTitle = "Leave without saving?";
    private const string ArchiveTitle = "Archive this article?";
    private static readonly Guid PaperplaneId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid OtherArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000009");

    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly NavigationManager _navigation;
    private readonly PortalUrlOptions _portal = new() { PublicUrl = "https://help.example.com" };

    public KbArticleEditorPageTests()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>(
            [TestData.Product("Paperplane", PaperplaneId), TestData.Product("Orbitly")]));
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<KbCategoryDto>>(
        [
            TestData.KbCategory("Getting started", "getting-started", null, TestData.GettingStartedId, sortOrder: 10),
            TestData.KbCategory("Account", "account", TestData.OrbitlyId, TestData.AccountCategoryId, sortOrder: 20),
        ]));
        _kb.GetAsync(TestData.ArticleId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(Stored()));
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(call => TestData.Ok(new KbPreviewResponse($"<p>{call.Arg<KbPreviewRequest>().BodyMarkdown}</p>")));
        Services.AddSingleton(_kb);
        Services.AddSingleton(_products);
        Services.AddKbFeatures();
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(_portal));
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private KbArticleDto _stored = TestData.KbArticle(productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId);

    private KbArticleDto Stored() => _stored;

    private IRenderedComponent<KbArticleEditorPage> RenderEditor(Guid? id = null)
    {
        _navigation.NavigateTo(id is null ? "/kb/new" : $"/kb/{id}");
        return Render<KbArticleEditorPage>(p => p.Add(c => c.Id, id));
    }

    private IRenderedComponent<KbArticleEditorPage> RenderStored(KbArticleDto? article = null)
    {
        _stored = article ?? _stored;
        return RenderEditor(_stored.Id);
    }

    private IEnumerable<object?[]> Calls(string method) => _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == method).Select(c => c.GetArguments());

    private IEnumerable<UpdateKbArticleRequest> Updates() => Calls(nameof(IKbClient.UpdateAsync)).Select(a => (UpdateKbArticleRequest)a[1]!);

    private IEnumerable<CreateKbArticleRequest> Creates() => Calls(nameof(IKbClient.CreateAsync)).Select(a => (CreateKbArticleRequest)a[0]!);

    private static string Value(IRenderedComponent<KbArticleEditorPage> cut, string id) => cut.Find($"#{id}").GetAttribute("value")!;

    private static void Save(IRenderedComponent<KbArticleEditorPage> cut) => cut.Find("form.ts-kb-form").Submit();

    private static IReadOnlyList<string> Alerts(IRenderedComponent<KbArticleEditorPage> cut) => [.. cut.FindAll("[role=alert]").Select(a => a.TextContent.Trim())];

    private static IRenderedComponent<ConfirmDialog> Dialog(IRenderedComponent<KbArticleEditorPage> cut, string title) =>
        cut.FindComponents<ConfirmDialog>().Single(d => d.Instance.Title == title);

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<KbArticleEditorPage> cut, string label) =>
        cut.FindAll(".ts-kb-actions button").Single(b => b.TextContent.Trim() == label);

    private void FillValidNewArticle(IRenderedComponent<KbArticleEditorPage> cut)
    {
        cut.Find("#ts-kb-product").Change(TestData.OrbitlyId.ToString());
        cut.Find("#ts-kb-category").Change(TestData.AccountCategoryId.ToString());
        cut.Find("#ts-kb-title").Input("  Reset your password  ");
        cut.Find("#ts-kb-summary").Input("How to reset it.");
        cut.Find("#ts-kb-body").Input("# Steps");
    }

    // ---- creating ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_new_article_asks_for_a_product_a_category_a_title_a_slug_a_summary_and_the_text_and_reads_no_article()
    {
        var cut = RenderEditor();

        cut.Find("h1").TextContent.ShouldBe("New article");
        cut.FindAll("#ts-kb-product option").Select(o => o.TextContent).ShouldBe(["Shared by every product", "Paperplane", "Orbitly"]);
        cut.FindAll("#ts-kb-category option").Select(o => o.TextContent).ShouldBe(["No category", "Getting started"]);
        cut.FindAll(".ts-kb-status .ts-pill").ShouldBeEmpty();
        cut.Find("#ts-kb-slug").ShouldNotBeNull();
        cut.Find("#ts-kb-body").ShouldNotBeNull();
        Button(cut, "Create draft").HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll(".ts-kb-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Create draft"]);
        _kb.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_slug_follows_the_title_until_the_agent_edits_it()
    {
        var cut = RenderEditor();

        cut.Find("#ts-kb-title").Input("Reset your password");
        Value(cut, "ts-kb-slug").ShouldBe("reset-your-password");

        cut.Find("#ts-kb-slug").Input("reset");
        cut.Find("#ts-kb-title").Input("Reset your account password");
        Value(cut, "ts-kb-slug").ShouldBe("reset");
    }

    [Fact]
    public void Choosing_a_product_offers_its_categories_and_the_shared_ones_and_changing_it_drops_a_category_it_may_not_use()
    {
        var cut = RenderEditor();

        cut.Find("#ts-kb-product").Change(TestData.OrbitlyId.ToString());
        cut.FindAll("#ts-kb-category option").Select(o => o.TextContent).ShouldBe(["No category", "Getting started", "Account"]);

        cut.Find("#ts-kb-category").Change(TestData.AccountCategoryId.ToString());
        cut.Find("#ts-kb-product").Change(string.Empty);

        cut.FindAll("#ts-kb-category option").Select(o => o.TextContent).ShouldBe(["No category", "Getting started"]);
        cut.Find("#ts-kb-category option[selected]").TextContent.ShouldBe("No category");
    }

    [Fact]
    public void Creating_sends_the_trimmed_text_never_cancels_and_goes_to_the_new_article_without_reading_it_again()
    {
        var created = TestData.KbArticle("Reset your password", "reset-your-password", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, id: OtherArticleId);
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(created));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => Creates().Count().ShouldBe(1));
        Creates().Single().ShouldBe(new CreateKbArticleRequest(TestData.OrbitlyId, TestData.AccountCategoryId, "reset-your-password", "Reset your password", "How to reset it.", "# Steps"));
        ((CancellationToken)Calls(nameof(IKbClient.CreateAsync)).Single()[1]!).CanBeCanceled.ShouldBeFalse();
        _navigation.Uri.ShouldEndWith($"/kb/{OtherArticleId}");
        ((BunitNavigationManager)_navigation).History.Last().Options.ReplaceHistoryEntry.ShouldBeTrue();
        StatusMessages.Current.ShouldBe("Created the draft Reset your password");
        _kb.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        cut.Find("h1").TextContent.ShouldBe("Reset your password");
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft");
    }

    [Fact]
    public void Saving_an_empty_form_sends_nothing_and_says_what_is_missing()
    {
        var cut = RenderEditor();

        Save(cut);

        Creates().ShouldBeEmpty();
        Alerts(cut).ShouldBe(["Enter a title.", "Enter a slug.", "Write the article before you save it."]);
        cut.Find("#ts-kb-title").GetAttribute("aria-invalid").ShouldBe("true");
        cut.Find("#ts-kb-title").GetAttribute("aria-describedby").ShouldBe("ts-kb-title-error");
    }

    [Fact]
    public void A_slug_that_is_not_lower_case_words_is_refused_before_it_is_sent()
    {
        var cut = RenderEditor();
        FillValidNewArticle(cut);
        cut.Find("#ts-kb-slug").Input("Reset Password");

        Save(cut);

        Creates().ShouldBeEmpty();
        Alerts(cut).ShouldBe(["Use lower-case letters, numbers and single hyphens, up to 80 characters."]);
    }

    [Fact]
    public void A_slug_that_is_taken_is_a_field_error_and_the_form_is_kept_for_another_try()
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.KbSlugTaken, "taken", ResultErrorKind.Conflict));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => Alerts(cut).ShouldBe(["Another article already uses this slug. Slugs are shared across products, so pick another."]));
        Value(cut, "ts-kb-title").ShouldBe("  Reset your password  ");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Steps");
        cut.Find("#ts-kb-slug").GetAttribute("aria-invalid").ShouldBe("true");

        cut.Find("#ts-kb-slug").Input("reset-your-password-2");
        Save(cut);
        cut.WaitForAssertion(() => Creates().Count().ShouldBe(2));
    }

    [Fact]
    public void A_category_the_api_refuses_for_the_product_is_a_field_error_on_the_category()
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.KbCategoryScopeMismatch, "no", ResultErrorKind.Conflict));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => Alerts(cut).ShouldBe(["A shared article can only use a shared category."]));
    }

    [Theory]
    [InlineData(ApiErrorCodes.KbCategoryNotFound, "categoryId", "That category no longer exists. Choose another.")]
    [InlineData("kb-category-scope-mismatch", "categoryId", "A shared article can only use a shared category.")]
    public void A_category_error_the_api_names_by_the_request_property_lands_on_the_category_select(string code, string target, string message)
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<KbArticleDto>.Failure(new ResultError(code, "from the API", ResultErrorKind.Validation, target)));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => Alerts(cut).ShouldBe([message]));
    }

    [Fact]
    public void A_400_names_its_field_and_a_message_for_no_field_is_shown_above_the_form()
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(Result<KbArticleDto>.Failure(
            new ResultError("validation-failed", "Too long.", ResultErrorKind.Validation, ApiFields.Title),
            [new ResultError("validation-failed", "Something else is wrong.", ResultErrorKind.Validation, "unknown-field")]));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => cut.Find("#ts-kb-title-error").TextContent.ShouldBe("Too long."));
        cut.Find("p.ts-form-error").TextContent.ShouldBe("Something else is wrong.");
    }

    [Fact]
    public void A_body_too_complex_on_save_lands_on_the_body_field_with_a_clear_message_and_keeps_the_form()
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<KbArticleDto>.Failure(new ResultError(ApiErrorCodes.KbBodyTooComplex, "from the API", ResultErrorKind.Validation, ApiFields.Body)));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => cut.Find("#ts-kb-body-error").TextContent.ShouldBe(KbEditorCopy.BodyTooComplex));
        cut.FindAll("p.ts-form-error").ShouldBeEmpty();
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Steps");
    }

    [Fact]
    public void A_body_too_complex_on_publish_lands_on_the_body_field()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(Result<KbArticleDto>.Failure(new ResultError(ApiErrorCodes.KbBodyTooComplex, "from the API", ResultErrorKind.Validation, ApiFields.Body)));
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find("#ts-kb-body-error").TextContent.ShouldBe(KbEditorCopy.BodyTooComplex));
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft");
    }

    [Fact]
    public void A_create_with_an_unknown_outcome_is_held_nothing_is_sent_twice_and_the_list_is_offered()
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The article may have been created."));
        cut.Find(".ts-conflict a").GetAttribute("href").ShouldBe("/kb");
        Button(cut, "Create draft").HasAttribute("disabled").ShouldBeTrue();
        Save(cut);
        Creates().Count().ShouldBe(1);
        Value(cut, "ts-kb-title").ShouldBe("  Reset your password  ");
    }

    // ---- editing -------------------------------------------------------------------------------------------------

    [Fact]
    public void An_article_is_loaded_with_its_fields_its_status_and_a_read_only_product_and_slug()
    {
        var cut = RenderStored();

        cut.Find("h1").TextContent.ShouldBe("Reset your password");
        Value(cut, "ts-kb-title").ShouldBe("Reset your password");
        cut.Find("#ts-kb-summary").GetAttribute("value").ShouldBe("How to reset it.");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Steps");
        cut.Find("#ts-kb-category option[selected]").TextContent.ShouldBe("Account");
        cut.Find("dl.ts-readonly").TextContent.ShouldContain("Orbitly");
        cut.Find("dl.ts-readonly code").TextContent.ShouldBe("reset-password");
        cut.FindAll("#ts-kb-product").ShouldBeEmpty();
        cut.FindAll("#ts-kb-slug").ShouldBeEmpty();
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft");
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void A_shared_article_says_shared_and_an_article_that_is_missing_says_so_and_asks_for_no_form()
    {
        var shared = RenderStored(TestData.KbArticle(productId: null, categoryId: TestData.GettingStartedId));
        shared.Find("dl.ts-readonly").TextContent.ShouldContain("Shared");

        _kb.GetAsync(OtherArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.KbArticleNotFound, "gone", ResultErrorKind.NotFound));
        var gone = RenderEditor(OtherArticleId);

        gone.Find(".ts-state--error").TextContent.ShouldContain("This article no longer exists.");
        gone.FindAll("form").ShouldBeEmpty();
        gone.FindAll(".ts-state--error button").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_says_so_with_the_api_message_and_retry_loads_again()
    {
        _kb.GetAsync(TestData.ArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>("api-error", "The API is unavailable."), TestData.Ok(Stored()));

        var cut = RenderEditor(TestData.ArticleId);

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load this article. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();
        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("Reset your password"));
    }

    [Fact]
    public void A_lookup_that_fails_is_a_failed_load_and_never_reads_as_article_not_found()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("product-not-found", "No such product.", ResultErrorKind.NotFound));

        var cut = RenderEditor(TestData.ArticleId);

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load this article.");
        cut.Markup.ShouldNotContain("no longer exists");
    }

    // Review Focus 4: the loaded version always travels.
    [Fact]
    public void Saving_sends_the_loaded_version_and_the_edited_fields_and_the_answer_replaces_the_form()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.KbArticle(call.Arg<UpdateKbArticleRequest>().Title!, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4)));
        var cut = RenderStored();

        cut.Find("#ts-kb-title").Input("  A better title ");
        cut.Find("#ts-kb-body").Input("# New steps");
        Button(cut, "Save").HasAttribute("disabled").ShouldBeFalse();
        Save(cut);

        cut.WaitForAssertion(() => Updates().Count().ShouldBe(1));
        Updates().Single().ShouldBe(new UpdateKbArticleRequest(TestData.AccountCategoryId, "A better title", "How to reset it.", "# New steps", 3u));
        ((CancellationToken)Calls(nameof(IKbClient.UpdateAsync)).Single()[2]!).CanBeCanceled.ShouldBeFalse();
        StatusMessages.Current.ShouldBe("Saved A better title");
        cut.WaitForAssertion(() => Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue());
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        cut.Find("h1").TextContent.ShouldBe("A better title");

        // The next save carries the new version.
        cut.Find("#ts-kb-title").Input("Another");
        Save(cut);
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(2));
        Updates().Last().Version.ShouldBe(4u);
    }

    [Fact]
    public void The_unsaved_mark_shows_while_the_form_differs_and_goes_when_the_text_is_typed_back()
    {
        var cut = RenderStored();
        cut.FindAll(".ts-dirty").ShouldBeEmpty();

        cut.Find("#ts-kb-title").Input("Reset your password!");
        cut.Find(".ts-dirty").TextContent.ShouldBe("Unsaved changes");

        cut.Find("#ts-kb-title").Input("Reset your password");
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
    }

    // Review Focus 4: an edit on a stale version is a 409, and the agent's text is never lost.
    [Fact]
    public void A_409_keeps_the_draft_shows_the_banner_and_turns_saving_off_until_the_agent_reloads()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ConcurrencyConflict, "This article changed.", ResultErrorKind.Conflict));
        var cut = RenderStored();
        cut.Find("#ts-kb-body").Input("# My careful edit");

        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("This article changed since you opened it."));
        cut.Find(".ts-conflict").TextContent.ShouldContain("Your edits are still in the form.");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# My careful edit");
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
        cut.Find(".ts-dirty").ShouldNotBeNull();
        Save(cut);
        Updates().Count().ShouldBe(1);
    }

    [Fact]
    public void Reloading_after_a_409_brings_in_the_latest_version_and_the_next_save_uses_its_version()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ConcurrencyConflict, "This article changed.", ResultErrorKind.Conflict), TestData.Ok(TestData.KbArticle(version: 9)));
        var cut = RenderStored();
        cut.Find("#ts-kb-body").Input("# My careful edit");
        Save(cut);
        cut.WaitForAssertion(() => cut.Find(".ts-conflict").ShouldNotBeNull());
        _stored = TestData.KbArticle(title: "Their title", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, body: "# Their text", version: 8);

        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("Their title"));
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Their text");
        cut.FindAll(".ts-conflict").ShouldBeEmpty();
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        cut.Find("#ts-kb-title").Input("Mine again");
        Save(cut);
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(2));
        Updates().Last().Version.ShouldBe(8u);
    }

    // Review Focus 4: an archived article that is edited becomes a draft again, and the editor says so.
    [Fact]
    public void Saving_an_archived_article_returns_it_to_draft_and_says_so()
    {
        var archived = TestData.KbArticle(status: KbArticleStatuses.Archived, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, publishedAt: TestData.Now.AddDays(-30));
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.KbArticle(status: KbArticleStatuses.Draft, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4, publishedAt: TestData.Now.AddDays(-30))));
        var cut = RenderStored(archived);
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Archived");

        cut.Find("#ts-kb-title").Input("Reopened");
        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft"));
        StatusMessages.Current.ShouldBe("Saved. This archived article is a draft again: publish it to put it back on the portal.");
        cut.Find(".ts-kb-status .ts-pill").GetAttribute("data-status").ShouldBe("Draft");
    }

    [Fact]
    public void A_save_with_an_unknown_outcome_is_held_until_a_reload_and_nothing_is_sent_twice()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ApiUnavailable, "down"));
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");
        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The change may have gone through."));
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
        Save(cut);
        Updates().Count().ShouldBe(1);
        Value(cut, "ts-kb-title").ShouldBe("Edited");

        _stored = TestData.KbArticle(title: "Edited", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4);
        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => cut.FindAll(".ts-conflict").ShouldBeEmpty());
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
    }

    [Fact]
    public void An_article_deleted_meanwhile_turns_a_save_into_the_gone_message()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.KbArticleNotFound, "gone", ResultErrorKind.NotFound));
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");

        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-state--error").TextContent.ShouldContain("This article no longer exists."));
        cut.FindAll("form").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_that_finishes_after_the_page_is_gone_changes_nothing_and_says_nothing()
    {
        var pending = new TaskCompletionSource<Result<KbArticleDto>>();
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");
        var saving = Task.Run(() => Save(cut), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(1));

        cut.Instance.Dispose();
        pending.SetResult(TestData.Ok(TestData.KbArticle("Edited", version: 4)));
        await saving;
        await cut.InvokeAsync(() => { });

        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public async Task A_save_that_finishes_after_the_agent_moved_to_another_article_says_so_once_and_leaves_the_new_screen_alone()
    {
        var pending = new TaskCompletionSource<Result<KbArticleDto>>();
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        _kb.GetAsync(OtherArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbArticle("The other one", "other", id: OtherArticleId, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId)));
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");
        var saving = Task.Run(() => Save(cut), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(1));

        // The leave guard would ask: the agent chose to leave, which this test does by changing the parameter directly.
        cut.Render(p => p.Add(c => c.Id, OtherArticleId));
        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("The other one"));
        pending.SetResult(TestData.Ok(TestData.KbArticle("Edited", version: 4)));
        await saving;

        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("Saved Edited"));
        Value(cut, "ts-kb-title").ShouldBe("The other one");
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void A_load_that_was_overtaken_by_another_article_never_replaces_the_newer_screen()
    {
        var slow = new TaskCompletionSource<Result<KbArticleDto>>();
        _kb.GetAsync(TestData.ArticleId, Arg.Any<CancellationToken>()).Returns(_ => slow.Task);
        _kb.GetAsync(OtherArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbArticle("Newer", "newer", id: OtherArticleId, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId)));
        var cut = RenderEditor(TestData.ArticleId);

        cut.Render(p => p.Add(c => c.Id, OtherArticleId));
        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("Newer"));
        slow.SetResult(TestData.Ok(TestData.KbArticle("Older", "older", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId)));

        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("Newer"));
        cut.Markup.ShouldNotContain("Older");
    }

    // ---- the preview ---------------------------------------------------------------------------------------------

    [Fact]
    public void Typing_in_the_article_text_previews_it_through_the_api_after_the_debounce()
    {
        var cut = RenderStored();
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p># Steps</p>"));

        cut.Find("#ts-kb-body").Input("# Steps\nMore");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p># Steps\nMore</p>"));
    }

    // ---- publish -------------------------------------------------------------------------------------------------

    [Fact]
    public void Publish_is_offered_only_once_the_article_is_saved_and_complete_and_the_hint_says_what_is_missing()
    {
        var cut = RenderStored();
        Button(cut, "Publish").HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll("#ts-kb-publish-hint").ShouldBeEmpty();

        cut.Find("#ts-kb-title").Input("Edited");
        Button(cut, "Publish").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#ts-kb-publish-hint").TextContent.ShouldBe("Save your changes before you publish.");

        var noCategory = RenderStored(TestData.KbArticle(productId: TestData.OrbitlyId, categoryId: null));
        Button(noCategory, "Publish").HasAttribute("disabled").ShouldBeTrue();
        noCategory.Find("#ts-kb-publish-hint").TextContent.ShouldBe("Choose a category before you publish.");
    }

    [Fact]
    public void Publishing_sends_the_loaded_version_shows_the_answer_and_a_portal_link_and_reads_nothing_again()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4, publishedAt: TestData.Now)));
        var cut = RenderStored();
        cut.FindAll(".ts-kb-portal-link").ShouldBeEmpty();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Published"));
        var call = Calls(nameof(IKbClient.PublishAsync)).Single();
        call[1].ShouldBe(3u);
        ((CancellationToken)call[2]!).CanBeCanceled.ShouldBeFalse();
        Calls(nameof(IKbClient.GetAsync)).Count().ShouldBe(1);
        StatusMessages.Current.ShouldBe("Published Reset your password");
        cut.FindAll(".ts-kb-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Save", "Archive"]);
        var link = cut.Find("a.ts-kb-portal-link");
        link.GetAttribute("href").ShouldBe("https://help.example.com/p/orbitly/kb/account/reset-password");
        link.GetAttribute("target").ShouldBe("_blank");
        link.GetAttribute("rel").ShouldBe("noopener noreferrer");
        link.TextContent.ShouldBe("View on portal");
    }

    [Fact]
    public void Publish_that_the_api_says_is_incomplete_names_the_field_and_changes_nothing()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(Result<KbArticleDto>.Failure(new ResultError(ApiErrorCodes.KbPublishIncomplete, "no category", ResultErrorKind.Validation, ApiFields.Category)));
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find("p.ts-form-error").TextContent.ShouldBe("Choose a category before you publish."));
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft");
        Calls(nameof(IKbClient.GetAsync)).Count().ShouldBe(1);
    }

    [Fact]
    public void A_publish_with_an_unknown_outcome_is_held_until_a_reload()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The publish may have gone through."));
        Button(cut, "Publish").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Archive").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Publish").Click();
        Calls(nameof(IKbClient.PublishAsync)).Count().ShouldBe(1);
    }

    // Review Focus 4: publishing an article another agent has changed meanwhile is a 409, never a publish of text nobody here has seen.
    [Fact]
    public void A_publish_on_a_stale_version_is_a_409_with_the_banner_and_nothing_is_published()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ConcurrencyConflict, "changed", ResultErrorKind.Conflict));
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("This article changed since you opened it."));
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft");
        Button(cut, "Publish").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Archive").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void An_article_someone_else_already_published_is_reloaded_with_a_message()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _stored = TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4);
            return TestData.Fail<KbArticleDto>("article-already-published", "already", ResultErrorKind.Conflict);
        });
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Published"));
        StatusMessages.Current.ShouldBe("Someone published this article already. It has been reloaded.");
    }

    [Fact]
    public void If_the_read_after_an_already_published_answer_fails_the_form_is_held_until_a_reload()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>("article-already-published", "already", ResultErrorKind.Conflict));
        _kb.GetAsync(TestData.ArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(Stored()), TestData.Fail<KbArticleDto>("api-error", "down"));
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The article could not be reloaded."));
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
    }

    // ---- archive -------------------------------------------------------------------------------------------------

    [Fact]
    public void Archive_asks_first_and_sends_nothing_until_the_dialog_is_confirmed()
    {
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));
        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();

        Button(cut, "Archive").Click();

        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeTrue();
        Dialog(cut, ArchiveTitle).Markup.ShouldContain("removed from the portal, from search and from the sitemap");
        _kb.DidNotReceive().ArchiveAsync(Arg.Any<Guid>(), Arg.Any<uint>(), Arg.Any<CancellationToken>());

        Dialog(cut, ArchiveTitle).FindAll(".ts-dialog-actions button")[0].Click();

        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();
        _kb.DidNotReceive().ArchiveAsync(Arg.Any<Guid>(), Arg.Any<uint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Confirming_archives_with_the_loaded_version_and_shows_the_answer_archived_without_a_portal_link()
    {
        _kb.ArchiveAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(TestData.KbArticle(status: KbArticleStatuses.Archived, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 5, publishedAt: TestData.Now)));
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, publishedAt: TestData.Now));
        cut.Find("a.ts-kb-portal-link").ShouldNotBeNull();
        Button(cut, "Archive").Click();

        Dialog(cut, ArchiveTitle).FindAll(".ts-dialog-actions button")[1].Click();

        cut.WaitForAssertion(() => cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Archived"));
        var call = Calls(nameof(IKbClient.ArchiveAsync)).Single();
        call[1].ShouldBe(3u);
        ((CancellationToken)call[2]!).CanBeCanceled.ShouldBeFalse();
        Calls(nameof(IKbClient.GetAsync)).Count().ShouldBe(1);
        StatusMessages.Current.ShouldBe("Archived Reset your password");
        cut.FindAll("a.ts-kb-portal-link").ShouldBeEmpty();
        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();
        cut.FindAll(".ts-kb-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Save", "Publish"]);
    }

    [Fact]
    public void An_archive_with_an_unknown_outcome_closes_the_dialog_and_holds_the_form_until_a_reload()
    {
        _kb.ArchiveAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ApiUnavailable, "down"));
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));
        Button(cut, "Archive").Click();

        Dialog(cut, ArchiveTitle).FindAll(".ts-dialog-actions button")[1].Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The archive may have gone through."));
        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();
        Button(cut, "Archive").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void An_archive_on_a_stale_version_is_a_409_with_the_banner_and_the_dialog_closes()
    {
        _kb.ArchiveAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ConcurrencyConflict, "changed", ResultErrorKind.Conflict));
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));
        Button(cut, "Archive").Click();

        Dialog(cut, ArchiveTitle).FindAll(".ts-dialog-actions button")[1].Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("This article changed since you opened it."));
        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Published");
    }

    [Fact]
    public void An_archived_article_offers_publish_again_and_no_archive()
    {
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Archived, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));

        cut.FindAll(".ts-kb-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Save", "Publish"]);
    }

    // ---- leaving -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_form_that_has_not_changed_leaves_without_asking()
    {
        var cut = RenderStored();
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeFalse();

        _navigation.NavigateTo("/queue");

        _navigation.Uri.ShouldEndWith("/queue");
        Dialog(cut, LeaveTitle).Instance.Open.ShouldBeFalse();
    }

    [Fact]
    public void A_changed_form_asks_before_an_in_app_move_and_before_the_browser_leaves()
    {
        var cut = RenderStored();
        cut.Find("#ts-kb-body").Input("# Unsaved");

        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();
        _navigation.NavigateTo("/queue");

        _navigation.Uri.ShouldEndWith($"/kb/{TestData.ArticleId}");
        Dialog(cut, LeaveTitle).Instance.Open.ShouldBeTrue();
        Dialog(cut, LeaveTitle).Markup.ShouldContain("If you leave now they are lost.");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Unsaved");
    }

    [Fact]
    public void Staying_keeps_the_page_and_the_text_and_leaving_goes_where_the_agent_was_going()
    {
        var cut = RenderStored();
        cut.Find("#ts-kb-body").Input("# Unsaved");
        _navigation.NavigateTo("/queue");

        Dialog(cut, LeaveTitle).FindAll(".ts-dialog-actions button")[0].Click();

        Dialog(cut, LeaveTitle).Instance.Open.ShouldBeFalse();
        _navigation.Uri.ShouldEndWith($"/kb/{TestData.ArticleId}");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Unsaved");

        _navigation.NavigateTo("/queue");
        Dialog(cut, LeaveTitle).FindAll(".ts-dialog-actions button")[1].Click();

        _navigation.Uri.ShouldEndWith("/queue");
    }

    [Fact]
    public void After_a_save_the_form_is_clean_and_leaving_does_not_ask()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.KbArticle("Edited", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4)));
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");
        Save(cut);
        cut.WaitForAssertion(() => cut.FindAll(".ts-dirty").ShouldBeEmpty());

        _navigation.NavigateTo("/kb");

        _navigation.Uri.ShouldEndWith("/kb");
        Dialog(cut, LeaveTitle).Instance.Open.ShouldBeFalse();
    }

    [Fact]
    public void A_new_article_with_text_asks_before_it_is_left_and_an_empty_one_does_not()
    {
        var empty = RenderEditor();
        _navigation.NavigateTo("/kb");
        _navigation.Uri.ShouldEndWith("/kb");
        Dialog(empty, LeaveTitle).Instance.Open.ShouldBeFalse();

        var typed = RenderEditor();
        typed.Find("#ts-kb-title").Input("Half written");
        _navigation.NavigateTo("/queue");

        Dialog(typed, LeaveTitle).Instance.Open.ShouldBeTrue();
        _navigation.Uri.ShouldEndWith("/kb/new");
    }

    // ---- View on portal ------------------------------------------------------------------------------------------

    [Fact]
    public void A_draft_has_no_portal_link_and_neither_does_anything_when_no_portal_address_is_set()
    {
        RenderStored().FindAll("a.ts-kb-portal-link").ShouldBeEmpty();

        _portal.PublicUrl = null;
        var published = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));

        published.FindAll("a.ts-kb-portal-link").ShouldBeEmpty();
        _portal.PublicUrl = string.Empty;
        RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId)).FindAll("a.ts-kb-portal-link").ShouldBeEmpty();
    }

    [Fact]
    public void A_shared_article_is_linked_under_the_first_product_by_name()
    {
        var cut = RenderStored(TestData.KbArticle("Welcome", "welcome", KbArticleStatuses.Published, productId: null, categoryId: TestData.GettingStartedId));

        cut.Find("a.ts-kb-portal-link").GetAttribute("href").ShouldBe("https://help.example.com/p/orbitly/kb/getting-started/welcome");
    }

    [Fact]
    public void The_portal_link_uses_the_saved_category_and_not_one_picked_but_not_saved()
    {
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));

        cut.Find("#ts-kb-category").Change(TestData.GettingStartedId.ToString());

        cut.Find("a.ts-kb-portal-link").GetAttribute("href").ShouldBe("https://help.example.com/p/orbitly/kb/account/reset-password");
    }
}
