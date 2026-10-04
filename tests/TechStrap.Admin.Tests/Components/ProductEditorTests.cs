using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 3 and 4 at the editor. A product edit never silently loses data: the update always carries the current IsActive (an omitted flag would deactivate the product), a stale Version is a
/// conflict and never an overwrite, and a failed save keeps every value on screen. An unsafe logo address (javascript:, data:, relative, plain http) never reaches the API and never reaches an img src.
/// </summary>
public sealed class ProductEditorTests : AdminPageTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly NavigationManager _navigation;

    public ProductEditorTests()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.ProductDetail(version: 7)));
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.ProductDetail(call.ArgAt<UpdateProductRequest>(1).Name!, active: call.ArgAt<UpdateProductRequest>(1).IsActive, version: 8)));
        _products.CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.ProductDetail(call.Arg<CreateProductRequest>().Name!, id: NewId, key: call.Arg<CreateProductRequest>().Key!)));
        Services.AddSingleton(_products);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private static readonly Guid NewId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000b1");

    private IRenderedComponent<ProductEditorPage> RenderEdit() => Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));

    private IRenderedComponent<ProductEditorPage> RenderNew() => Render<ProductEditorPage>();

    private static string Value(IRenderedComponent<ProductEditorPage> cut, string id) => cut.Find($"#{id}").GetAttribute("value")!;

    private static void Type(IRenderedComponent<ProductEditorPage> cut, string id, string text) => cut.Find($"#{id}").Input(text);

    private static string? FieldError(IRenderedComponent<ProductEditorPage> cut, string id) => cut.FindAll($"#{id}-error").SingleOrDefault()?.TextContent;

    // The requests the page sent, read from the substitute's own record of its calls (a later Returns on the same call must not hide them).
    private IReadOnlyList<UpdateProductRequest> Updates =>
        [.. _products.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IProductsClient.UpdateAsync)).Select(c => (UpdateProductRequest)c.GetArguments()[1]!)];

    private IReadOnlyList<CreateProductRequest> Creates =>
        [.. _products.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IProductsClient.CreateAsync)).Select(c => (CreateProductRequest)c.GetArguments()[0]!)];

    private static void Save(IRenderedComponent<ProductEditorPage> cut) => cut.Find("form").Submit();

    // ---- loading -------------------------------------------------------------------------------------------------

    [Fact]
    public void An_edit_shows_the_saved_values_with_the_key_and_prefix_read_only()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(
            logo: "https://cdn.example.com/logo.png", from: "support@orbitly.test", replyTo: "help@orbitly.test", accent: "#1D4ED8")));

        var cut = RenderEdit();

        cut.Find("h1").TextContent.ShouldBe("Orbitly");
        Value(cut, "ts-product-name").ShouldBe("Orbitly");
        Value(cut, "ts-product-display").ShouldBe("Orbitly");
        Value(cut, "ts-product-logo").ShouldBe("https://cdn.example.com/logo.png");
        Value(cut, "ts-product-accent").ShouldBe("#1D4ED8");
        Value(cut, "ts-product-from").ShouldBe("support@orbitly.test");
        Value(cut, "ts-product-reply").ShouldBe("help@orbitly.test");
        cut.Find(".ts-readonly").TextContent.ShouldContain("orbitly");
        cut.Find(".ts-readonly").TextContent.ShouldContain("ORB");
        cut.FindAll("#ts-product-key").ShouldBeEmpty();
        cut.FindAll("#ts-product-prefix").ShouldBeEmpty();
        cut.Find("img.ts-accent-preview-logo").GetAttribute("src").ShouldBe("https://cdn.example.com/logo.png");
        cut.Find("button[type=submit]").HasAttribute("disabled").ShouldBeTrue("nothing changed yet");
    }

    [Fact]
    public void A_product_the_api_does_not_know_says_so_with_no_retry_and_no_form()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ProductNotFound, "No such product.", ResultErrorKind.NotFound));

        var cut = RenderEdit();

        cut.Find("[role=alert]").TextContent.ShouldContain("This product no longer exists.");
        cut.FindAll("[role=alert] button").ShouldBeEmpty();
        cut.FindAll("form.ts-form").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<ProductDto>("api-error", "The API is unavailable."), TestData.Ok(TestData.ProductDetail()));

        var cut = RenderEdit();
        cut.Find("[role=alert]").TextContent.ShouldContain("Couldn't load this product. The API is unavailable.");
        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => Value(cut, "ts-product-name").ShouldBe("Orbitly"));
    }

    [Fact]
    public void An_id_that_is_not_a_guid_is_reported_as_not_found_and_calls_nothing()
    {
        var notFound = 0;
        _navigation.OnNotFound += (_, _) => notFound++;

        Render<ProductEditorPage>(p => p.Add(c => c.Id, "not-a-guid"));

        notFound.ShouldBe(1);
        _products.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked()
    {
        AsAgent();

        var cut = RenderEdit();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("form.ts-form").ShouldBeEmpty();
        _products.ReceivedCalls().ShouldBeEmpty();
    }

    // ---- Review Focus 3: no silent data loss ---------------------------------------------------------------------

    [Fact]
    public void Saving_a_changed_name_sends_the_current_IsActive_the_loaded_Version_and_every_branding_field_unchanged()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(
            active: true, version: 7, logo: "https://cdn.example.com/logo.png", from: "support@orbitly.test", replyTo: "help@orbitly.test")));
        var cut = RenderEdit();

        Type(cut, "ts-product-name", "Orbitly Cloud");
        Save(cut);

        var sent = Updates.ShouldHaveSingleItem();
        sent.Name.ShouldBe("Orbitly Cloud");
        sent.IsActive.ShouldBeTrue("an omitted flag would deactivate the product");
        sent.Version.ShouldBe(7u);
        sent.Branding.ShouldBe(new ProductBrandingRequest("Orbitly", "https://cdn.example.com/logo.png", "#1D4ED8", "support@orbitly.test", "help@orbitly.test"));
        _products.Received(1).UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
    }

    [Fact]
    public void An_inactive_product_stays_inactive_when_only_its_name_is_edited()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(active: false, version: 7)));
        var cut = RenderEdit();

        Type(cut, "ts-product-name", "Orbitly Legacy");
        Save(cut);

        Updates.ShouldHaveSingleItem().IsActive.ShouldBeFalse();
    }

    [Fact]
    public void The_active_switch_sends_the_flipped_value_and_blank_optional_fields_go_as_null()
    {
        var cut = RenderEdit();

        cut.Find("#ts-product-active").Change(false);
        Type(cut, "ts-product-logo", "   ");
        Save(cut);

        var sent = Updates.ShouldHaveSingleItem();
        sent.IsActive.ShouldBeFalse();
        sent.Branding.LogoPath.ShouldBeNull();
        sent.Branding.FromAddress.ShouldBeNull();
        sent.Branding.ReplyTo.ShouldBeNull();
    }

    [Fact]
    public void After_a_successful_save_the_form_shows_the_saved_product_and_the_next_save_carries_the_new_Version()
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Orbitly Cloud");

        Save(cut);

        StatusMessages.Current.ShouldBe("Saved Orbitly Cloud");
        cut.Find("button[type=submit]").HasAttribute("disabled").ShouldBeTrue("saved, nothing changed since");
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        Type(cut, "ts-product-name", "Orbitly Cloud 2");
        cut.Find(".ts-dirty").TextContent.ShouldBe("Unsaved changes");
        Save(cut);
        Updates.Select(u => u.Version).ShouldBe([7u, 8u]);
    }

    [Fact]
    public void A_stale_version_raises_the_conflict_banner_keeps_every_typed_value_and_overwrites_nothing()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ConcurrencyConflict, "This product changed since you opened it.", ResultErrorKind.Conflict));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "My new name");
        Type(cut, "ts-product-accent", "#FF0000");

        Save(cut);

        var banner = cut.Find(".ts-conflict[role=alert]");
        banner.TextContent.ShouldContain("This product changed since you opened it.");
        banner.TextContent.ShouldContain("Your edits are still on screen");
        banner.QuerySelector("button")!.TextContent.ShouldBe("Reload");
        Value(cut, "ts-product-name").ShouldBe("My new name");
        Value(cut, "ts-product-accent").ShouldBe("#FF0000");
        StatusMessages.Current.ShouldBeNull();
        Updates.Count.ShouldBe(1);
    }

    [Fact]
    public void Reload_after_a_conflict_shows_the_saved_version_and_the_next_save_uses_its_Version()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict), TestData.Ok(TestData.ProductDetail("Theirs 2", version: 10)));
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(version: 7)), TestData.Ok(TestData.ProductDetail("Theirs", version: 9)));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Mine");
        Save(cut);

        cut.Find(".ts-conflict button").Click();

        cut.FindAll(".ts-conflict").ShouldBeEmpty();
        Value(cut, "ts-product-name").ShouldBe("Theirs");
        Type(cut, "ts-product-name", "Theirs 2");
        Save(cut);
        Updates.Select(u => u.Version).ShouldBe([7u, 9u]);
    }

    [Theory]
    [InlineData("name", "ts-product-name")]
    [InlineData("display-name", "ts-product-display")]
    [InlineData("logo-path", "ts-product-logo")]
    [InlineData("accent-colour", "ts-product-accent")]
    [InlineData("from-address", "ts-product-from")]
    [InlineData("reply-to", "ts-product-reply")]
    public void A_field_error_from_the_api_appears_at_its_field_by_its_kebab_case_target_and_every_value_stays(string target, string inputId)
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Failure(new ResultError("x-invalid", "The server says no.", ResultErrorKind.Validation, target)));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed name");
        Type(cut, "ts-product-display", "Changed display");

        Save(cut);

        FieldError(cut, inputId).ShouldBe("The server says no.");
        cut.Find($"#{inputId}").GetAttribute("aria-invalid").ShouldBe("true");
        Value(cut, "ts-product-name").ShouldBe("Changed name");
        Value(cut, "ts-product-display").ShouldBe("Changed display");
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public void An_error_the_form_has_no_field_for_is_shown_once_above_the_form_in_the_apis_words()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>("something-else", "Something unusual happened.", ResultErrorKind.Validation));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");

        Save(cut);

        cut.Find("p.ts-form-error[role=alert]").TextContent.ShouldBe("Something unusual happened.");
        Value(cut, "ts-product-name").ShouldBe("Changed");
    }

    [Fact]
    public void An_uncertain_save_keeps_the_values_and_never_claims_nothing_happened()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");

        Save(cut);

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The save may have gone through.");
        alert.TextContent.ShouldNotContain("Nothing was changed");
        Value(cut, "ts-product-name").ShouldBe("Changed");
    }

    [Fact]
    public void A_second_submit_while_the_first_is_running_sends_once_and_the_form_is_inert_meanwhile()
    {
        var gate = new TaskCompletionSource<Result<ProductDto>>();
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");

        Save(cut);
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("button[type=submit]").HasAttribute("disabled").ShouldBeTrue();
        Save(cut);

        _products.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IProductsClient.UpdateAsync)).ShouldBe(1);
        gate.SetResult(TestData.Ok(TestData.ProductDetail("Changed", version: 8)));
        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("Saved Changed"));
    }

    [Fact]
    public void A_save_that_finishes_after_the_page_is_gone_is_not_cancelled_and_touches_nothing()
    {
        var gate = new TaskCompletionSource<Result<ProductDto>>();
        CancellationToken seen = default;
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Do<CancellationToken>(t => seen = t)).Returns(gate.Task);
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");
        Save(cut);

        cut.FindComponent<ProductEditorContent>().Instance.Dispose();
        gate.SetResult(TestData.Ok(TestData.ProductDetail("Changed", version: 8)));

        seen.CanBeCanceled.ShouldBeFalse("a write on its way is never abandoned");
        StatusMessages.Current.ShouldBeNull("the page was disposed before the answer, so it does not report on it");
    }

    // ---- client-side validation ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("1D4ED8")]
    public void A_malformed_accent_is_refused_on_blur_and_on_submit_and_never_sent(string accent)
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-accent", accent);
        cut.Find("#ts-product-accent").Blur();
        FieldError(cut, "ts-product-accent").ShouldBe("Use a colour like #1D4ED8: a # and six hex digits.");
        Save(cut);

        Updates.ShouldBeEmpty();
        Value(cut, "ts-product-accent").ShouldBe(accent);
    }

    [Theory]
    [InlineData("#1D4ED8")]
    [InlineData("#1d4ed8")]
    [InlineData("")]
    public void A_valid_or_blank_accent_is_sent(string accent)
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-accent", accent);
        Save(cut);

        Updates.ShouldHaveSingleItem().Branding.AccentColour.ShouldBe(accent.Length == 0 ? null : accent);
    }

    [Fact]
    public void A_low_contrast_accent_shows_only_an_information_note_and_still_saves()
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-accent", "#FFEB3B");

        cut.Find("p[role=note]").TextContent.ShouldContain("low contrast");
        Save(cut);
        Updates.ShouldHaveSingleItem().Branding.AccentColour.ShouldBe("#FFEB3B");
    }

    [Fact]
    public void A_missing_name_or_display_name_or_a_bad_email_blocks_the_save_with_a_field_error_each()
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-name", "  ");
        Type(cut, "ts-product-display", "");
        Type(cut, "ts-product-from", "not-an-email");
        Save(cut);

        FieldError(cut, "ts-product-name").ShouldBe("Enter a name.");
        FieldError(cut, "ts-product-display").ShouldBe("Enter the name customers see.");
        FieldError(cut, "ts-product-from").ShouldBe("Enter a valid email address.");
        Updates.ShouldBeEmpty();
    }

    // ---- Review Focus 4: unsafe logo addresses ------------------------------------------------------------------

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("/images/logo.png")]
    [InlineData("//cdn.example.com/logo.png")]
    [InlineData("ftp://example.com/logo.png")]
    [InlineData("http://example.com/logo.png")]
    public void An_unsafe_logo_address_is_refused_never_previewed_and_never_sent(string logo)
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", logo);
        cut.Find("#ts-product-logo").Blur();

        FieldError(cut, "ts-product-logo").ShouldBe("Use a full https:// address for the logo.");
        cut.FindAll("img").ShouldBeEmpty("the preview never loads an address that failed the rule");
        cut.Markup.ShouldNotContain("src=\"javascript");
        cut.Markup.ShouldNotContain("src=\"data:");
        Save(cut);
        Updates.ShouldBeEmpty();
        _products.DidNotReceive().UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void An_https_logo_is_previewed_and_sent_trimmed()
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", "  https://cdn.example.com/logo.png  ");

        cut.Find("img.ts-accent-preview-logo").GetAttribute("src").ShouldBe("https://cdn.example.com/logo.png");
        Save(cut);
        Updates.ShouldHaveSingleItem().Branding.LogoPath.ShouldBe("https://cdn.example.com/logo.png");
    }

    [Fact]
    public void Plain_http_to_localhost_is_accepted_but_plain_http_to_anywhere_else_is_not()
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", "http://localhost:5000/logo.png");
        Save(cut);

        Updates.ShouldHaveSingleItem().Branding.LogoPath.ShouldBe("http://localhost:5000/logo.png");
    }

    [Theory]
    [InlineData("https://user:secret@cdn.example.com/logo.png")]
    [InlineData("https://cdn.example.com/my logo.png")]
    public void An_address_with_user_info_or_spaces_is_refused_by_the_shared_rule(string logo)
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", logo);
        Save(cut);

        FieldError(cut, "ts-product-logo").ShouldNotBeNull();
        Updates.ShouldBeEmpty();
    }

    [Fact]
    public void The_api_refusing_a_logo_address_the_editor_accepted_is_shown_at_the_logo_field()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Failure(new ResultError(ApiErrorCodes.LogoPathInvalid, "The logo address must be a full https address.", ResultErrorKind.Validation, ApiFields.LogoPath)));
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", "https://blocked.example/logo.png");
        Save(cut);

        FieldError(cut, "ts-product-logo").ShouldBe("The logo address must be a full https address.");
        Value(cut, "ts-product-logo").ShouldBe("https://blocked.example/logo.png");
    }

    // ---- create --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_new_product_asks_for_key_name_prefix_and_branding_and_has_no_active_switch()
    {
        var cut = RenderNew();

        cut.Find("h1").TextContent.ShouldBe("New product");
        foreach (var id in new[] { "ts-product-key", "ts-product-name", "ts-product-prefix", "ts-product-display", "ts-product-logo", "ts-product-accent", "ts-product-from", "ts-product-reply" })
        {
            cut.FindAll($"#{id}").Count.ShouldBe(1, id);
        }

        cut.FindAll("#ts-product-active").ShouldBeEmpty();
        cut.Find("button[type=submit]").TextContent.ShouldBe("Create product");
        _products.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void A_new_product_is_validated_before_it_is_sent()
    {
        var cut = RenderNew();

        Type(cut, "ts-product-key", "Bad Key");
        Type(cut, "ts-product-prefix", "o");
        Save(cut);

        FieldError(cut, "ts-product-key").ShouldBe("Use lower-case letters, numbers and single hyphens, up to 40 characters.");
        FieldError(cut, "ts-product-name").ShouldBe("Enter a name.");
        FieldError(cut, "ts-product-prefix").ShouldBe("Use 2 to 10 capital letters or numbers, starting with a letter.");
        FieldError(cut, "ts-product-display").ShouldBe("Enter the name customers see.");
        Creates.ShouldBeEmpty();
    }

    [Fact]
    public void Creating_a_product_sends_the_request_and_goes_to_its_keys_with_a_status_message()
    {
        var cut = RenderNew();
        Type(cut, "ts-product-key", "nimbus");
        Type(cut, "ts-product-name", "Nimbus");
        Type(cut, "ts-product-prefix", "NIM");
        Type(cut, "ts-product-display", "Nimbus Cloud");
        Type(cut, "ts-product-accent", "#0F766E");

        Save(cut);

        Creates.ShouldHaveSingleItem().ShouldBe(new CreateProductRequest("nimbus", "Nimbus", "NIM", new ProductBrandingRequest("Nimbus Cloud", null, "#0F766E", null, null)));
        _navigation.Uri.ShouldEndWith($"/settings/products/{NewId}/keys");
        StatusMessages.Current.ShouldBe("Created Nimbus");
        _products.Received(1).CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
    }

    [Fact]
    public void A_taken_key_or_prefix_is_shown_above_the_form_and_the_values_stay()
    {
        _products.CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ProductKeyTaken, "Taken.", ResultErrorKind.Conflict));
        var cut = RenderNew();
        Type(cut, "ts-product-key", "nimbus");
        Type(cut, "ts-product-name", "Nimbus");
        Type(cut, "ts-product-prefix", "NIM");
        Type(cut, "ts-product-display", "Nimbus");

        Save(cut);

        cut.Find("p.ts-form-error").TextContent.ShouldBe("Another product already uses this key or ticket number prefix.");
        Value(cut, "ts-product-key").ShouldBe("nimbus");
        _navigation.Uri.ShouldNotContain("/keys");
    }

    // ---- fix round 1 ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ApiErrorCodes.ApiTimeout)]
    [InlineData(ApiErrorCodes.ApiError)]
    [InlineData(ApiErrorCodes.ApiUnavailable)]
    public void An_uncertain_create_keeps_the_form_and_every_value_says_so_and_offers_no_reload(string code)
    {
        _products.CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(code, "Lost."));
        var cut = RenderNew();
        Type(cut, "ts-product-key", "nimbus");
        Type(cut, "ts-product-name", "Nimbus");
        Type(cut, "ts-product-prefix", "NIM");
        Type(cut, "ts-product-display", "Nimbus Cloud");

        Save(cut);

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("We could not confirm the product was created.");
        alert.QuerySelector("button").ShouldBeNull();
        alert.QuerySelector("a")!.GetAttribute("href").ShouldBe("/settings/products");
        Value(cut, "ts-product-key").ShouldBe("nimbus");
        Value(cut, "ts-product-name").ShouldBe("Nimbus");
        Value(cut, "ts-product-prefix").ShouldBe("NIM");
        Value(cut, "ts-product-display").ShouldBe("Nimbus Cloud");
        cut.FindAll("form.ts-form").Count.ShouldBe(1);
        _products.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void After_a_successful_save_the_form_shows_the_logo_the_api_returned_not_the_typed_text()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.ProductDetail(version: 8, logo: "https://cdn.example/")));
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", "HTTPS://Cdn.Example");
        Save(cut);

        Value(cut, "ts-product-logo").ShouldBe("https://cdn.example/");
        cut.Find("img.ts-accent-preview-logo").GetAttribute("src").ShouldBe("https://cdn.example/");
    }

    [Theory]
    [InlineData("key")]
    [InlineData("number-prefix")]
    public void On_an_edit_a_server_error_for_a_field_the_form_does_not_show_appears_above_the_form(string target)
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Failure(new ResultError("x-invalid", "Permanent field refused.", ResultErrorKind.Validation, target)));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");

        Save(cut);

        cut.Find("p.ts-form-error[role=alert]").TextContent.ShouldBe("Permanent field refused.");
    }

    [Fact]
    public void A_new_Id_on_the_same_component_drops_the_banners_errors_and_dirty_state_of_the_old_product()
    {
        var other = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c3");
        _products.GetAsync(other, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail("Other", id: other, key: "other", prefix: "OTH")));
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Mine");
        Save(cut);
        cut.FindAll(".ts-conflict").Count.ShouldBe(1);
        Type(cut, "ts-product-accent", "bad");
        cut.Find("#ts-product-accent").Blur();
        cut.FindAll(".ts-field-error").Count.ShouldBe(1);

        cut.Render(p => p.Add(c => c.Id, other.ToString()));

        cut.WaitForAssertion(() => Value(cut, "ts-product-name").ShouldBe("Other"));
        cut.FindAll(".ts-conflict").ShouldBeEmpty();
        cut.FindAll(".ts-field-error").ShouldBeEmpty();
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        cut.FindAll("p.ts-form-error").ShouldBeEmpty();
    }

    [Fact]
    public void A_new_invalid_Id_on_the_same_component_renders_no_form()
    {
        var cut = RenderEdit();
        var notFound = 0;
        _navigation.OnNotFound += (_, _) => notFound++;

        cut.Render(p => p.Add(c => c.Id, "not-a-guid"));

        notFound.ShouldBe(1);
        cut.FindAll("form").ShouldBeEmpty();
    }

    [Fact]
    public void An_invalid_Id_renders_no_blank_form()
    {
        var cut = Render<ProductEditorPage>(p => p.Add(c => c.Id, "not-a-guid"));

        cut.FindAll("form").ShouldBeEmpty();
    }

    [Fact]
    public void After_an_uncertain_create_the_button_is_disabled_and_a_second_submit_sends_no_second_post()
    {
        _products.CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ApiTimeout, "Lost."));
        var cut = RenderNew();
        Type(cut, "ts-product-key", "nimbus");
        Type(cut, "ts-product-name", "Nimbus");
        Type(cut, "ts-product-prefix", "NIM");
        Type(cut, "ts-product-display", "Nimbus Cloud");
        Save(cut);

        cut.Find("button[type=submit]").HasAttribute("disabled").ShouldBeTrue();
        Save(cut);

        Creates.Count.ShouldBe(1, "a resend would meet a 409 product-key-taken that says the wrong thing");
        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("We could not confirm the product was created.");
        cut.FindAll("p.ts-form-error").ShouldBeEmpty();
    }

    [Fact]
    public void A_slow_answer_for_the_product_that_was_left_never_replaces_the_form_of_the_product_now_shown()
    {
        var other = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c4");
        var older = new TaskCompletionSource<Result<ProductDto>>();
        var newer = new TaskCompletionSource<Result<ProductDto>>();
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(older.Task);
        _products.GetAsync(other, Arg.Any<CancellationToken>()).Returns(newer.Task);
        var cut = RenderEdit();

        cut.Render(p => p.Add(c => c.Id, other.ToString()));
        newer.SetResult(TestData.Ok(TestData.ProductDetail("Other", id: other, key: "other", prefix: "OTH")));
        cut.WaitForAssertion(() => Value(cut, "ts-product-name").ShouldBe("Other"));
        older.SetResult(TestData.Ok(TestData.ProductDetail("Orbitly")));
        cut.Render();

        Value(cut, "ts-product-name").ShouldBe("Other");
        cut.FindAll(".ts-loading").ShouldBeEmpty();
    }

    [Fact]
    public void A_slow_load_for_the_old_product_never_fills_the_new_product_form_or_leaves_it_loading()
    {
        var older = new TaskCompletionSource<Result<ProductDto>>();
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(older.Task);
        var cut = RenderEdit();
        cut.FindAll(".ts-loading").Count.ShouldBe(1);

        cut.Render(p => p.Add(c => c.Id, null));
        older.SetResult(TestData.Ok(TestData.ProductDetail("Orbitly")));
        cut.Render();

        cut.FindAll(".ts-loading").ShouldBeEmpty();
        Value(cut, "ts-product-name").ShouldBe(string.Empty);
        cut.FindAll("#ts-product-key").Count.ShouldBe(1, "the new-product form is shown");
    }

    [Fact]
    public void A_save_for_the_old_product_that_finishes_after_the_Id_changed_touches_neither_the_new_form_nor_its_busy_state()
    {
        var other = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c5");
        var gate = new TaskCompletionSource<Result<ProductDto>>();
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        _products.GetAsync(other, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail("Other", id: other, key: "other", prefix: "OTH")));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");
        Save(cut);

        cut.Render(p => p.Add(c => c.Id, other.ToString()));
        cut.WaitForAssertion(() => Value(cut, "ts-product-name").ShouldBe("Other"));
        gate.SetResult(TestData.Ok(TestData.ProductDetail("Changed", version: 8)));
        cut.Render();

        Value(cut, "ts-product-name").ShouldBe("Other");
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
    }
}
