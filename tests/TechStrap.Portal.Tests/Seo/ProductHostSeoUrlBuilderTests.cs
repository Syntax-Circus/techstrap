using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using SyntaxCircus.Blazor.Seo;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Seo;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>The host-aware <see cref="ISeoUrlBuilder"/> (D-050 amendment): on a product host an address is on that product's stored host, elsewhere the package's builder decides.</summary>
public sealed class ProductHostSeoUrlBuilderTests
{
    private const string PublicUrl = "https://portal.test";
    private const string DragonHost = "support.dragonpoop.com";

    private static ProductHostContext OnDragon() => new() { Key = "dragon-poop", Host = DragonHost };

    private static (ProductHostSeoUrlBuilder Builder, SeoUrlBuilder Inner, DefaultHttpContext Http) Build(ProductHostContext host)
    {
        var http = new DefaultHttpContext();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        var inner = new SeoUrlBuilder(Options.Create(new SeoOptions { BaseUrl = PublicUrl }), accessor);
        var portal = Options.Create(new PortalOptions { PublicUrl = PublicUrl });
        return (new ProductHostSeoUrlBuilder(inner, host, portal, accessor), inner, http);
    }

    [Fact]
    public void A_relative_path_on_a_product_host_uses_that_host()
    {
        var (builder, _, _) = Build(OnDragon());

        builder.AbsoluteUrl("/x").ShouldBe("https://support.dragonpoop.com/x");
    }

    [Fact]
    public void A_relative_path_on_the_default_host_uses_the_public_url()
    {
        var (builder, _, _) = Build(new ProductHostContext());

        builder.AbsoluteUrl("/x").ShouldBe("https://portal.test/x");
    }

    [Fact]
    public void An_absolute_url_passes_through_on_either_host()
    {
        Build(OnDragon()).Builder.AbsoluteUrl("https://other.example/y").ShouldBe("https://other.example/y");
        Build(new ProductHostContext()).Builder.AbsoluteUrl("https://other.example/y").ShouldBe("https://other.example/y");
    }

    [Fact]
    public void A_blank_path_on_a_product_host_is_the_host_root()
    {
        var (builder, _, _) = Build(OnDragon());

        builder.AbsoluteUrl("").ShouldBe("https://support.dragonpoop.com");
        builder.AbsoluteUrl("  ").ShouldBe("https://support.dragonpoop.com");
        builder.AbsoluteUrl("x").ShouldBe("https://support.dragonpoop.com/x");
    }

    [Fact]
    public void The_canonical_for_the_current_request_on_a_product_host_is_the_clean_path()
    {
        var (builder, _, http) = Build(OnDragon());

        http.Request.Path = "/p/dragon-poop/kb/a";
        builder.CanonicalForCurrentRequest(null).ShouldBe("https://support.dragonpoop.com/kb/a");
        http.Request.Path = "/p/dragon-poop";
        builder.CanonicalForCurrentRequest(null).ShouldBe("https://support.dragonpoop.com");
    }

    [Fact]
    public void A_canonical_override_is_resolved_like_any_url()
    {
        var (builder, _, _) = Build(OnDragon());

        builder.CanonicalForCurrentRequest("/kb/z").ShouldBe("https://support.dragonpoop.com/kb/z");
    }

    [Fact]
    public void The_canonical_on_the_default_host_delegates()
    {
        var (builder, inner, http) = Build(new ProductHostContext());
        http.Request.Path = "/p/who-flung-poo/kb";

        builder.CanonicalForCurrentRequest(null).ShouldBe(inner.CanonicalForCurrentRequest(null));
    }
}
