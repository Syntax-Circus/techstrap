using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Skins;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The interim "Skin (JSON)" field of the product editor (D-053): untouched text sends no skin, changed valid JSON sends the parsed skin, blanked text on a product that had a skin sends an empty skin
/// (the clear), and invalid JSON never reaches the API.
/// </summary>
public sealed class ProductEditorSkinTests : AdminPageTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();

    public ProductEditorSkinTests()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.ProductDetail(version: 7)));
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.ProductDetail(call.ArgAt<UpdateProductRequest>(1).Name!, version: 8)));
        _products.CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.ProductDetail(call.Arg<CreateProductRequest>().Name!, id: Guid.NewGuid(), key: call.Arg<CreateProductRequest>().Key!)));
        Services.AddSingleton(_products);
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
    public void The_field_is_a_twelve_row_textarea_without_spellcheck_inside_a_details_element()
    {
        var cut = RenderEdit();

        var field = cut.Find("#ts-product-skin");
        field.TagName.ShouldBe("TEXTAREA");
        field.GetAttribute("rows").ShouldBe("12");
        field.GetAttribute("spellcheck").ShouldBe("false");
        field.ParentElement!.ParentElement!.TagName.ShouldBe("DETAILS");
        cut.Find("label[for=ts-product-skin]").TextContent.ShouldBe(ProductsCopy.SkinLabel);
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
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin(Pack: "paper", Radius: "square"));
    }

    [Fact]
    public void Blanking_a_stored_skin_sends_an_empty_skin_to_clear_it()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Pack: "slate"))));
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "   ");
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
        Type(cut, "ts-product-skin", "{ \"ink\": \"#EEEEEE\" }");
        Save(cut);

        FieldError(cut, "ts-product-skin")!.ShouldContain("ink on background");
        cut.Find("#ts-product-skin").GetAttribute("aria-describedby").ShouldBe("ts-product-skin-error");
    }
}
