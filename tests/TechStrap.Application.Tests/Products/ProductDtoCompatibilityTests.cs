using TechStrap.Contracts.Products;

namespace TechStrap.Application.Tests.Products;

/// <summary>Contracts 0.3.0 adds trailing optional parameters only: the 0.2.0 positional argument lists still compile and the new values default as documented.</summary>
public sealed class ProductDtoCompatibilityTests
{
    [Fact]
    public void The_0_2_0_argument_lists_still_construct_every_record_with_the_documented_defaults()
    {
        var branding = new ProductBrandingDto("Orbitly", null, "#1F6FEB", "#FFFFFF", "#1F6FEB", null, null);
        var product = new ProductDto(Guid.Empty, "orbitly", "Orbitly", "ORB", true, branding, 1, "support.orbitly.test");
        var create = new CreateProductRequest("orbitly", "Orbitly", "ORB", null, null);
        var update = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", null, null, null, null), true, 1, null);
        var publicDto = new PublicProductDto("orbitly", "Orbitly", null, "#1F6FEB", "#FFFFFF", "#1F6FEB", null);
        var summary = new PublicProductSummaryDto("orbitly", "Orbitly", null);

        branding.Tagline.ShouldBeNull();
        branding.UploadedLogoUrl.ShouldBeNull();
        product.ListedOnLanding.ShouldBeTrue();
        create.ListedOnLanding.ShouldBeNull();
        update.ListedOnLanding.ShouldBeNull();
        update.Branding.Tagline.ShouldBeNull();
        publicDto.Tagline.ShouldBeNull();
        summary.ShouldSatisfyAllConditions(
            s => s.Tagline.ShouldBeNull(),
            s => s.LogoUrl.ShouldBeNull(),
            s => s.AccentColour.ShouldBeNull(),
            s => s.ListedOnLanding.ShouldBeTrue());
    }
}
