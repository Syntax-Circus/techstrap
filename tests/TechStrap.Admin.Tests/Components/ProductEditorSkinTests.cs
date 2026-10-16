using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Components.Ui;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Skins;
using TechStrap.Contracts.Settings;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Structured appearance and advanced JSON share an applied draft while preserving nullable inheritance and safe product writes.
/// </summary>
public sealed class ProductEditorSkinTests : AdminPageTest
{
    private readonly IAppearanceSettingsClient _appearance = Substitute.For<IAppearanceSettingsClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();

    public ProductEditorSkinTests()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.ProductDetail(version: 7)));
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.ProductDetail(call.ArgAt<UpdateProductRequest>(1).Name!, version: 8)));
        _products.CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.ProductDetail(call.Arg<CreateProductRequest>().Name!, id: Guid.NewGuid(), key: call.Arg<CreateProductRequest>().Key!)));
        Services.AddSingleton(_products);
        _appearance.GetAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(new SiteSettingsDto("classic", 1)));
        Services.AddSingleton(_appearance);
    }

    private IRenderedComponent<ProductEditorPage> RenderEdit() => Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));

    private static string Value(IRenderedComponent<ProductEditorPage> cut, string id) => cut.Find($"#{id}").GetAttribute("value")!;

    private static void Type(IRenderedComponent<ProductEditorPage> cut, string id, string text) => cut.Find($"#{id}").Input(text);

    private static string? FieldError(IRenderedComponent<ProductEditorPage> cut, string id) => cut.FindAll($"#{id}-error").SingleOrDefault()?.TextContent;

    private static void Save(IRenderedComponent<ProductEditorPage> cut) => cut.Find("form").Submit();

    private IReadOnlyList<UpdateProductRequest> Updates =>
        [.. _products.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IProductsClient.UpdateAsync)).Select(c => (UpdateProductRequest)c.GetArguments()[1]!)];

    private IReadOnlyList<CreateProductRequest> Creates =>
        [.. _products.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IProductsClient.CreateAsync)).Select(c => (CreateProductRequest)c.GetArguments()[0]!)];

    [Fact]
    public void Removing_a_skin_requires_confirmation_and_sends_explicit_clear()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Pack: "paper", Radius: "round"))));
        var cut = RenderEdit();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Remove product skin").Click();
        Value(cut, "ts-product-skin").ShouldContain("paper");
        var dialog = cut.FindComponents<ConfirmDialog>().Single(d => d.Instance.Title == "Remove product skin?");
        dialog.Instance.Open.ShouldBeTrue();
        dialog.Find("button.btn-danger").Click();
        Save(cut);
        Updates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin());
    }

    [Fact]
    public async Task Unapplied_json_participates_in_the_leave_page_guard()
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "{ bad");
        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/products"));
        cut.FindComponents<ConfirmDialog>().Single(d => d.Instance.Title == "Leave unsaved product changes?").Instance.Open.ShouldBeTrue();
        Value(cut, "ts-product-skin").ShouldBe("{ bad");
    }

    [Fact]
    public async Task An_older_default_response_cannot_replace_a_successful_retry()
    {
        var pending = new TaskCompletionSource<Result<SiteSettingsDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _appearance.GetAsync(Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cut = RenderEdit();
        _appearance.GetAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(new SiteSettingsDto("paper", 4)));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Retry appearance default")).Click();
        cut.FindComponent<TechStrap.Admin.Features.Appearance.PortalAppearancePreview>().Instance.DefaultPack.ShouldBe("paper");
        await cut.InvokeAsync(() => pending.SetResult(TestData.Ok(new SiteSettingsDto("midnight", 3))));
        cut.WaitForAssertion(() => cut.FindComponent<TechStrap.Admin.Features.Appearance.PortalAppearancePreview>().Instance.DefaultPack.ShouldBe("paper"));
    }

    [Fact]
    public void Failed_default_load_allows_an_unrelated_save_but_holds_inherited_appearance()
    {
        _appearance.GetAsync(Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Failure(new ResultError("unavailable", "Unavailable", ResultErrorKind.Validation)));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Keep this name");
        cut.Find("#ts-skin-radius").Change("round");
        Save(cut);
        Updates.ShouldBeEmpty();
        FieldError(cut, "ts-product-skin")!.ShouldContain("contrast is unknown");
        cut.Find("#ts-product-name").GetAttribute("value").ShouldBe("Keep this name");
        cut.FindAll(".ts-appearance-preview").ShouldBeEmpty();

        cut.Find("#ts-skin-radius").Change("");
        Save(cut);
        Updates.ShouldHaveSingleItem().Name.ShouldBe("Keep this name");
        Updates[0].Skin.ShouldBeNull();
    }

    [Fact]
    public void Retrying_default_preserves_product_and_appearance_drafts()
    {
        _appearance.GetAsync(Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Failure(new ResultError("unavailable", "Unavailable", ResultErrorKind.Validation)));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Keep this name");
        cut.Find("#ts-skin-radius").Change("round");
        _appearance.GetAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(new SiteSettingsDto("paper", 4)));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Retry appearance default")).Click();
        Save(cut);
        Updates.ShouldHaveSingleItem().Name.ShouldBe("Keep this name");
        Updates[0].Skin.ShouldBe(new ProductSkin(Radius: "round"));
    }

    [Fact]
    public void Discarding_raw_json_retains_structured_skin_and_reenables_controls()
    {
        var cut = RenderEdit();
        cut.Find("#ts-skin-pack").Change("paper");
        Type(cut, "ts-product-skin", "{ bad");
        cut.Find("#ts-skin-pack").ParentElement!.ParentElement!.HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#ts-skin-discard").Click();
        Save(cut);
        Updates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin(Pack: "paper"));
    }

    [Fact]
    public void Resetting_one_token_preserves_other_explicit_and_inherited_tokens()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Pack: "paper", Radius: "round", Header: "band"))));
        var cut = RenderEdit();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Reset Radius").Click();
        Save(cut);
        Updates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin(Pack: "paper", Header: "band"));
    }

    [Fact]
    public void Low_contrast_structured_changes_block_keyboard_save()
    {
        var cut = RenderEdit();
        cut.Find("#ts-skin-ink").Input("#FFFFFF");
        Save(cut);
        Updates.ShouldBeEmpty();
        FieldError(cut, "ts-product-skin")!.ShouldContain("ink/background");
    }

    [Fact]
    public void All_skin_tokens_have_structured_controls()
    {
        var cut = RenderEdit();
        foreach (var field in new[] { "pack", "background", "surface", "ink", "muted", "border", "brand", "chrome", "focus", "headingFont", "bodyFont", "radius", "borderWidth", "shadow", "button", "header" })
            cut.Find($"#ts-skin-{field}").ShouldNotBeNull();
    }

    [Fact]
    public void Unapplied_json_blocks_keyboard_save_and_keeps_visual_draft()
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "{ \"pack\": \"paper\" }");
        Save(cut);
        Updates.ShouldBeEmpty();
        cut.Find("#ts-skin-pack").GetAttribute("value").ShouldBeNullOrEmpty();
        cut.Markup.ShouldContain("Apply JSON");
    }

    [Fact]
    public void Structured_pack_preserves_inherited_tokens()
    {
        var cut = RenderEdit();
        cut.Find("#ts-skin-pack").Change("paper");
        Save(cut);
        Updates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin(Pack: "paper"));
    }

    [Fact]
    public void The_field_is_a_twelve_row_textarea_without_spellcheck_inside_a_details_element()
    {
        var cut = RenderEdit();

        var field = cut.Find("#ts-product-skin");
        field.TagName.ShouldBe("TEXTAREA");
        field.GetAttribute("rows").ShouldBe("12");
        field.GetAttribute("spellcheck").ShouldBe("false");
        field.ParentElement!.ParentElement!.TagName.ShouldBe("DETAILS");
        cut.Find("label[for=ts-product-skin]").TextContent.ShouldBe("Skin JSON");
    }

    [Fact]
    public void A_stored_skin_is_shown_as_json_in_the_field()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Pack: "slate", Brand: "#112233"))));

        var cut = RenderEdit();

        Value(cut, "ts-product-skin").ShouldContain("\"pack\": \"slate\"");
        Value(cut, "ts-product-skin").ShouldContain("\"brand\": \"#112233\"");
    }

    [Fact]
    public void An_untouched_field_sends_no_skin()
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Orbitly Cloud");
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBeNull();
    }

    [Fact]
    public void An_untouched_stored_skin_sends_no_skin()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Pack: "slate"))));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Orbitly Cloud");
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBeNull();
    }

    [Fact]
    public void Pasted_json_is_sent_as_the_parsed_skin()
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "{ \"pack\": \"paper\", \"radius\": \"square\" }");
        if (cut.Find("#ts-skin-apply").HasAttribute("disabled") is false) cut.Find("#ts-skin-apply").Click();
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin(Pack: "paper", Radius: "square"));
    }

    [Fact]
    public void Blanking_a_stored_skin_sends_an_empty_skin_to_clear_it()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Pack: "slate"))));
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "   ");
        if (cut.Find("#ts-skin-apply").HasAttribute("disabled") is false) cut.Find("#ts-skin-apply").Click();
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin());
    }

    [Fact]
    public void A_new_product_sends_the_parsed_skin()
    {
        var cut = Render<ProductEditorPage>();
        Type(cut, "ts-product-key", "orbitly");
        Type(cut, "ts-product-name", "Orbitly");
        Type(cut, "ts-product-prefix", "ORB");
        Type(cut, "ts-product-display", "Orbitly");
        Type(cut, "ts-product-skin", "{ \"pack\": \"midnight\" }");
        if (cut.Find("#ts-skin-apply").HasAttribute("disabled") is false) cut.Find("#ts-skin-apply").Click();
        Save(cut);

        Creates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin(Pack: "midnight"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"shine\": \"yes\" }")]
    [InlineData("{ \"background\": \"red\" }")]
    public void Invalid_json_blocks_the_save_at_the_field(string json)
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", json);
        if (cut.Find("#ts-skin-apply").HasAttribute("disabled") is false) cut.Find("#ts-skin-apply").Click();
        Save(cut);

        FieldError(cut, "ts-product-skin").ShouldNotBeNullOrEmpty();
        Updates.ShouldBeEmpty();
    }

    [Fact]
    public void An_api_contrast_error_is_shown_at_the_skin_field()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Failure(new ResultError("skin-contrast-invalid", "ink on background is too low.", ResultErrorKind.Validation, "ink/background")));
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "{ \"pack\": \"paper\" }");
        if (cut.Find("#ts-skin-apply").HasAttribute("disabled") is false) cut.Find("#ts-skin-apply").Click();
        Save(cut);

        FieldError(cut, "ts-product-skin")!.ShouldContain("ink on background");
        cut.Find("#ts-product-skin").GetAttribute("aria-describedby").ShouldBe("ts-product-skin-error");
    }

    [Fact]
    public void Blanking_the_field_does_not_collapse_the_section()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Pack: "slate"))));
        var cut = RenderEdit();
        cut.Find("details.ts-product-skin").HasAttribute("open").ShouldBeTrue();

        Type(cut, "ts-product-skin", "");

        cut.Find("details.ts-product-skin").HasAttribute("open").ShouldBeTrue();
    }

    [Fact]
    public void A_stored_skin_that_fails_the_rules_does_not_block_an_unrelated_save()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Brand: "red"))));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Orbitly Cloud");
        if (cut.Find("#ts-skin-apply").HasAttribute("disabled") is false) cut.Find("#ts-skin-apply").Click();
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBeNull();
    }

    [Fact]
    public void Whitespace_on_a_product_without_a_skin_sends_no_skin()
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "   ");
        if (cut.Find("#ts-skin-apply").HasAttribute("disabled") is false) cut.Find("#ts-skin-apply").Click();
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBeNull();
    }

    [Fact]
    public void An_api_skin_too_long_error_is_shown_at_the_skin_field()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Failure(new ResultError("skin-too-long", "The skin is too long.", ResultErrorKind.Validation, "skin")));
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "{ \"pack\": \"paper\" }");
        if (cut.Find("#ts-skin-apply").HasAttribute("disabled") is false) cut.Find("#ts-skin-apply").Click();
        Save(cut);

        FieldError(cut, "ts-product-skin")!.ShouldContain("too long");
    }
}
