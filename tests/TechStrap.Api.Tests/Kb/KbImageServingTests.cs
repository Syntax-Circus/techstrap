using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;
using TechStrap.Application.Knowledge;

namespace TechStrap.Api.Tests.Kb;

/// <summary>
/// Review Focus 2 on the wire: <c>GET /kb-images/{name}</c> is anonymous and serves an image with headers that stop a browser treating it as
/// anything else, never serves a name the store could not have written, and leaks nothing about the caller.
/// </summary>
public sealed class KbImageServingTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, .. new byte[40]];

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-kbserving-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, recursive: true);
        }
    }

    private ApiFactory Factory() => new(settings: new Dictionary<string, string?> { ["Storage:Local:RootPath"] = _storage });

    private static async Task<StoredKbImage> StoreAsync(ApiFactory factory, byte[] bytes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IKbImageStore>().SaveAsync(new IncomingKbImage(bytes.Length, new MemoryStream(bytes)), Ct);
        return result.Value;
    }

    [Fact]
    public async Task An_anonymous_caller_gets_the_image_with_safe_headers_and_a_long_immutable_cache()
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/kb-images/{stored.FileName}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        response.Content.Headers.ContentLength.ShouldBe(Png.Length);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("default-src 'none'");
        csp.ShouldContain("sandbox");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=31536000, immutable");
        response.Headers.GetValues("Cross-Origin-Resource-Policy").ShouldBe(["cross-origin"]);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        response.Headers.Contains("WWW-Authenticate").ShouldBeFalse();
    }

    [Fact]
    public async Task A_bearer_token_makes_no_difference_and_nothing_about_the_caller_is_echoed()
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        using var anonymous = factory.CreateClient();
        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        using var garbage = factory.CreateClient();
        garbage.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        foreach (var client in new[] { anonymous, agent, garbage })
        {
            using var response = await client.GetAsync($"/kb-images/{stored.FileName}", Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.Select(header => header.Key).ShouldNotContain("Vary");
            response.Headers.Contains("WWW-Authenticate").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task An_unknown_well_formed_name_is_a_404_that_is_not_cached()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/kb-images/0123456789abcdef0123456789abcdef.png", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
    }

    [Theory]
    [InlineData("/kb-images/..%2F..%2Fappsettings.json")]
    [InlineData("/kb-images/%2e%2e%2fsecret.png")]
    [InlineData("/kb-images/..%5C..%5Csecret.png")]
    [InlineData("/kb-images/%252e%252e%252fsecret.png")]
    [InlineData("/kb-images/0123456789abcdef0123456789abcdef")]
    [InlineData("/kb-images/0123456789abcdef0123456789abcdef.svg")]
    [InlineData("/kb-images/0123456789ABCDEF0123456789ABCDEF.png")]
    [InlineData("/kb-images/kb-images%2F0123456789abcdef0123456789abcdef.png")]
    public async Task A_name_the_store_could_not_have_written_is_a_404_and_reads_nothing_outside_the_prefix(string path)
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        File.WriteAllBytes(Path.Combine(_storage, "secret.png"), Png);
        File.WriteAllBytes(Path.Combine(_storage, "appsettings.json"), "{}"u8.ToArray());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("{}");
    }

    [Theory]
    [InlineData("/kb-images/")]
    [InlineData("/kb-images")]
    [InlineData("/kb-images/a/b.png")]
    [InlineData("/kb-images/..;/secret.png")]
    [InlineData("/kb-images/attachments/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef")]
    public async Task A_path_that_matches_no_image_route_never_returns_a_file(string path)
    {
        await using var factory = Factory();
        await StoreAsync(factory, Png);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        // The Api answers an unmatched anonymous path with its fallback (401) or a 404; either way no file.
        new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized }.ShouldContain(response.StatusCode);
        response.Content.Headers.ContentType?.MediaType.ShouldNotStartWith("image/");
    }

    [Fact]
    public async Task A_head_request_gets_the_headers_and_no_body()
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/kb-images/{stored.FileName}"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_head_request_for_an_unknown_image_is_a_404_that_is_not_cached_and_has_no_body()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/kb-images/0123456789abcdef0123456789abcdef.png"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_trailing_slash_is_a_404()
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/kb-images/{stored.FileName}/", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_get_is_served()
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        using var client = factory.CreateClient();

        using var post = await client.PostAsync($"/kb-images/{stored.FileName}", new ByteArrayContent(Png), Ct);
        using var delete = await client.DeleteAsync($"/kb-images/{stored.FileName}", Ct);

        new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized }.ShouldContain(post.StatusCode);
        new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized }.ShouldContain(delete.StatusCode);
        File.Exists(Path.Combine(_storage, stored.Key)).ShouldBeTrue();
    }
}
