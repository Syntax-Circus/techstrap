namespace TechStrap.Application.Products;

/// <summary>The Api's public address as the Worker knows it (<c>TECHSTRAP_API_PUBLIC_URL</c>, optional): the base of an uploaded logo's address in emails (D-052). Blank means emails show the linked logo only.</summary>
public sealed class ProductLogoUrlOptions
{
    public const string PublicUrlKey = "TECHSTRAP_API_PUBLIC_URL";

    public string PublicUrl { get; set; } = string.Empty;
}
