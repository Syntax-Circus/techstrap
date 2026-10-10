using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tests.Products;

public sealed class ProductSkinTests
{
    private static Product New() => Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), true, 1);

    [Fact]
    public void A_restored_product_has_no_skin() => New().SkinJson.ShouldBeNull();

    [Fact]
    public void Setting_the_skin_stores_the_json_and_blank_clears_it()
    {
        var product = New();

        product.SetSkinJson("{\"pack\":\"slate\"}").IsSuccess.ShouldBeTrue();
        product.SkinJson.ShouldBe("{\"pack\":\"slate\"}");

        product.SetSkinJson("   ").IsSuccess.ShouldBeTrue();
        product.SkinJson.ShouldBeNull();
    }

    [Fact]
    public void The_skin_json_may_be_2000_characters_but_not_2001()
    {
        var product = New();

        product.SetSkinJson(new string('a', 2000)).IsSuccess.ShouldBeTrue();
        var tooLong = product.SetSkinJson(new string('a', 2001));

        tooLong.IsFailure.ShouldBeTrue();
        tooLong.Error!.Code.ShouldBe("skin-too-long");
        tooLong.Error.Target.ShouldBe("skin");
        DomainLimits.SkinJsonMaxLength.ShouldBe(2000);
    }

    [Fact]
    public void Restore_carries_the_stored_skin()
    {
        var product = Product.Restore(Guid.CreateVersion7(), "o", "O", "OO", ProductBranding.Restore("O", null, "#1F6FEB", null, null), true, 1, null, true, "{\"pack\":\"paper\"}");

        product.SkinJson.ShouldBe("{\"pack\":\"paper\"}");
    }
}
