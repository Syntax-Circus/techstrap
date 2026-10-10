using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The logo button: a wrong type or an oversize image is refused before anything is sent, and an image the API refuses changes nothing (D-052).</summary>
public sealed class ProductLogoUploadButtonTests : AdminComponentTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly List<ProductDto> _changed = [];
    private readonly List<bool> _uploading = [];

    public ProductLogoUploadButtonTests()
    {
        _products.UploadLogoAsync(TestData.OrbitlyId, Arg.Any<ProductLogoFile>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(version: 8)));
        Services.AddSingleton(_products);
    }

    private IRenderedComponent<ProductLogoUploadButton> RenderButton() =>
        Render<ProductLogoUploadButton>(p => p
            .Add(b => b.ProductId, TestData.OrbitlyId)
            .Add(b => b.OnChanged, saved => _changed.Add(saved))
            .Add(b => b.OnUploadingChanged, busy => _uploading.Add(busy)));

    private static void Pick(IRenderedComponent<ProductLogoUploadButton> cut, string name, int bytes = 4, string type = "image/png") =>
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[bytes], name, null, type));

    private int Uploads() => _products.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IProductsClient.UploadLogoAsync));

    [Fact]
    public void A_gif_is_refused_before_anything_is_sent()
    {
        var cut = RenderButton();

        Pick(cut, "logo.gif", type: "image/gif");

        cut.Find("[role=alert]").TextContent.ShouldBe(ProductsCopy.LogoTypeNotAllowed);
        Uploads().ShouldBe(0);
        _changed.ShouldBeEmpty();
    }

    [Fact]
    public void An_image_over_one_mebibyte_is_refused_before_anything_is_sent()
    {
        var cut = RenderButton();

        Pick(cut, "huge.png", bytes: (int)ProductLogoLimits.MaxBytes + 1);

        cut.Find("[role=alert]").TextContent.ShouldBe(ProductsCopy.LogoTooLarge);
        Uploads().ShouldBe(0);
        _changed.ShouldBeEmpty();
    }

    [Fact]
    public void A_valid_png_is_uploaded_once_and_the_returned_product_is_raised()
    {
        var cut = RenderButton();

        Pick(cut, "logo.png", bytes: 8);

        cut.WaitForAssertion(() => _changed.ShouldHaveSingleItem().Version.ShouldBe(8u));
        Uploads().ShouldBe(1);
        var file = (ProductLogoFile)_products.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IProductsClient.UploadLogoAsync)).GetArguments()[1]!;
        file.FileName.ShouldBe("logo.png");
        file.ContentType.ShouldBe("image/png");
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void An_image_the_api_refuses_shows_that_copy_and_raises_nothing()
    {
        _products.UploadLogoAsync(TestData.OrbitlyId, Arg.Any<ProductLogoFile>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ProductLogoTypeNotAllowed, "from the API"));
        var cut = RenderButton();

        Pick(cut, "logo.png");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldBe(ProductsCopy.LogoTypeNotAllowed));
        _changed.ShouldBeEmpty();
    }

    [Fact]
    public void The_owner_is_told_the_upload_started_and_then_ended()
    {
        var cut = RenderButton();

        Pick(cut, "logo.png");

        cut.WaitForAssertion(() => _uploading.ShouldBe([true, false]));
    }
}
