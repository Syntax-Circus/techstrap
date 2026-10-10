using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// PHASE-09c adds a success-only <c>Cache-Control</c> rule to the shared header wiring for the Portal's help center. The Admin shares that wiring, so this pins that nothing it serves, an ordinary page, an error page or
/// a download, ever gets a public cache header from it: an agent's page must never be kept by a shared cache.
/// </summary>
public sealed class PublicCacheHeaderPinTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_admin_page_error_page_or_download_is_given_a_public_cache_header()
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var anonymous = factory.CreateClient();
        using var signedIn = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        foreach (var path in new[] { "/not-found", "/error", "/no-such-page", "/health/live" })
        {
            using var response = await anonymous.GetAsync(path, Ct);
            response.Headers.CacheControl?.Public.ShouldNotBe(true, path);
        }

        using var download = await signedIn.GetAsync($"/attachments/{id}", Ct);
        download.Headers.CacheControl!.NoStore.ShouldBeTrue();
        download.Headers.CacheControl.Public.ShouldNotBe(true);
    }
}
