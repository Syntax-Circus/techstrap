using TechStrap.Portal.Products;

namespace TechStrap.Portal.Seo;

/// <summary>What the help-centre pages tell <c>SeoHead</c> that is the same on every page.</summary>
public static class KbSeo
{
    /// <summary>A Portal asset: the Open Graph image of a product with no logo (the package would otherwise fall back to the bare site address, which is a page and not an image).</summary>
    public const string FallbackImage = "/icon-512.png";

    /// <summary>The product's logo when it has an acceptable one (https, already checked), else the Portal's own image.</summary>
    public static string Image(ProductThemeViewModel theme) => theme.LogoUrl ?? FallbackImage;
}
