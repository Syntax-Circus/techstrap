using System.Net;

namespace TechStrap.Portal.Tests;

/// <summary>The libman-restored fonts must be served by the running host, not only exist on disk.</summary>
public sealed class FontHostingTests
{
    public static TheoryData<string> FontFiles() =>
    [
        "ibm-plex-sans/files/ibm-plex-sans-latin-400-normal.woff2",
        "ibm-plex-sans/files/ibm-plex-sans-latin-500-normal.woff2",
        "ibm-plex-sans/files/ibm-plex-sans-latin-600-normal.woff2",
        "ibm-plex-mono/files/ibm-plex-mono-latin-400-normal.woff2",
        "ibm-plex-mono/files/ibm-plex-mono-latin-500-normal.woff2",
        "ibm-plex-mono/files/ibm-plex-mono-latin-600-normal.woff2",
        "nunito/files/nunito-latin-400-normal.woff2",
        "nunito/files/nunito-latin-600-normal.woff2",
        "nunito/files/nunito-latin-700-normal.woff2",
        "atkinson-hyperlegible/files/atkinson-hyperlegible-latin-400-normal.woff2",
        "atkinson-hyperlegible/files/atkinson-hyperlegible-latin-700-normal.woff2",
        "source-serif-4/files/source-serif-4-latin-400-normal.woff2",
        "source-serif-4/files/source-serif-4-latin-600-normal.woff2",
        "pixelify-sans/files/pixelify-sans-latin-400-normal.woff2",
        "pixelify-sans/files/pixelify-sans-latin-700-normal.woff2",
    ];

    [Theory]
    [MemberData(nameof(FontFiles))]
    public async Task Font_file_is_served_as_woff2(string relativePath)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/fonts/" + relativePath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, relativePath);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("font/woff2");
    }
}
