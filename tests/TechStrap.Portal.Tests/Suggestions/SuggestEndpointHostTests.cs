using System.Net;
using System.Text.Json;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Suggestions;

/// <summary>
/// P09-T07 at the host: the Portal-hosted suggest adapter behind the contact page's element (D-017 exempt: no workflow, plain JSON). It answers <c>[{title, snippet, href}]</c> with plain text and a Portal link
/// built from the product the visitor is on, at most five, for a query cut at the API's 200 characters; a blank query is an empty list without a call; the API's 429 is passed through and any other failure is an
/// empty list; the response is never stored and never indexed. Every call forwards the visitor's address through a real page request behind a trusted proxy (Review Focus 5), and the visitor's text never reaches a log.
/// </summary>
public sealed class SuggestEndpointHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Api = "/api/public/kb/paperplane/search";
    private const string Visitor = FormTestKit.Visitor;

    private static PublicKbSearchResultDto Hit(int n, string? title = null, string? snippet = null, string? slug = null) =>
        new(slug ?? $"article-{n}", title ?? $"Article {n}", snippet ?? $"About {n}.", "guides", "Guides", n % 2 == 0 ? null : "paperplane");

    private static PagedResponse<PublicKbSearchResultDto> Page(params PublicKbSearchResultDto[] hits) => new(hits, 1, 5, hits.Length);

    private static PortalFactory Host(PagedResponse<PublicKbSearchResultDto>? page = null)
    {
        var factory = FormTestKit.Factory(product: false);
        factory.Api.OnJson(HttpMethod.Get, Api, page ?? Page(Hit(1), Hit(2)));
        return factory;
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> GetAsync(PortalFactory factory, string pathAndQuery)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(pathAndQuery, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement);
    }

    [Fact]
    public async Task A_search_returns_title_snippet_and_a_link_built_from_the_visitors_product()
    {
        await using var factory = Host();

        var (response, body) = await GetAsync(factory, "/p/paperplane/suggest?q=reset%20password");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        body.GetArrayLength().ShouldBe(2);
        body[0].GetProperty("title").GetString().ShouldBe("Article 1");
        body[0].GetProperty("snippet").GetString().ShouldBe("About 1.");
        body[0].GetProperty("href").GetString().ShouldBe("/p/paperplane/kb/guides/article-1");
        body[1].GetProperty("href").GetString().ShouldBe("/p/paperplane/kb/guides/article-2", "a shared article is linked under the product the visitor is on");
        body[0].EnumerateObject().Select(p => p.Name).ShouldBe(["title", "snippet", "href"]);
    }

    [Fact]
    public async Task The_api_is_asked_for_five_through_the_read_client_with_the_visitors_address_and_the_text_escaped()
    {
        await using var factory = Host();

        await GetAsync(factory, "/p/paperplane/suggest?q=a%26category%3Dsecret%23x");

        var sent = factory.Api.Requests.ShouldHaveSingleItem();
        sent.Path.ShouldBe(Api);
        sent.Query.ShouldBe("?q=a%26category%3Dsecret%23x&pageSize=5");
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.TicketToken.ShouldBeNull();
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task Text_in_the_answer_stays_text_in_the_json_and_the_href_never_comes_from_the_api()
    {
        var evil = new PublicKbSearchResultDto("../../x", "<img src=x onerror=alert(1)>", "<script>alert(1)</script>", "javascript:alert(1)", "<b>Guides</b>", null);
        await using var factory = Host(Page(evil));

        var (_, body) = await GetAsync(factory, "/p/paperplane/suggest?q=anything");

        body[0].GetProperty("title").GetString().ShouldBe("<img src=x onerror=alert(1)>");
        body[0].GetProperty("snippet").GetString().ShouldBe("<script>alert(1)</script>");
        body[0].GetProperty("href").GetString().ShouldBe("/p/paperplane/kb/javascript%3Aalert%281%29/..%2F..%2Fx", "every segment is escaped, so the link is a path of this site and nothing else");
        body[0].GetProperty("href").GetString()!.ShouldStartWith("/p/paperplane/kb/");
    }

    [Fact]
    public async Task A_hit_that_names_another_product_is_still_linked_under_the_visitors_product_key()
    {
        var other = new PublicKbSearchResultDto("shared-article", "Shared", "Snippet.", "guides", "Guides", "otherproduct");
        await using var factory = Host(Page(other));

        var (_, body) = await GetAsync(factory, "/p/paperplane/suggest?q=shared");

        body[0].GetProperty("href").GetString().ShouldBe("/p/paperplane/kb/guides/shared-article");
    }

    [Fact]
    public async Task At_most_five_items_come_back()
    {
        await using var factory = Host(Page([.. Enumerable.Range(1, 8).Select(n => Hit(n))]));

        var (_, body) = await GetAsync(factory, "/p/paperplane/suggest?q=many");

        body.GetArrayLength().ShouldBe(5);
    }

    [Fact]
    public async Task A_hit_with_no_slug_or_no_title_is_left_out()
    {
        await using var factory = Host(Page(Hit(1, slug: ""), Hit(2, title: " "), Hit(3)));

        var (_, body) = await GetAsync(factory, "/p/paperplane/suggest?q=x3x");

        body.GetArrayLength().ShouldBe(1);
        body[0].GetProperty("title").GetString().ShouldBe("Article 3");
    }

    [Theory]
    [InlineData("/p/paperplane/suggest")]
    [InlineData("/p/paperplane/suggest?q=")]
    [InlineData("/p/paperplane/suggest?q=%20%20%09")]
    public async Task A_blank_or_missing_query_is_an_empty_list_and_no_call(string path)
    {
        await using var factory = Host();

        var (response, body) = await GetAsync(factory, path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetArrayLength().ShouldBe(0);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_long_query_is_cut_at_the_apis_200_characters()
    {
        await using var factory = Host();

        await GetAsync(factory, "/p/paperplane/suggest?q=" + new string('x', 500));

        var query = factory.Api.Requests.ShouldHaveSingleItem().Query;
        query.ShouldBe("?q=" + new string('x', KbLimits.MaxSearchTextChars) + "&pageSize=5");
    }

    [Fact]
    public async Task A_cut_never_splits_a_surrogate_pair()
    {
        await using var factory = Host();
        var text = new string('a', KbLimits.MaxSearchTextChars - 1) + char.ConvertFromUtf32(0x1F600);

        await GetAsync(factory, "/p/paperplane/suggest?q=" + Uri.EscapeDataString(text));

        factory.Api.Requests.ShouldHaveSingleItem().Query.ShouldBe("?q=" + new string('a', KbLimits.MaxSearchTextChars - 1) + "&pageSize=5");
    }

    [Theory]
    [InlineData("/p/Not_A_Slug/suggest?q=printer")]
    [InlineData("/p/paper%20plane/suggest?q=printer")]
    [InlineData("/p/..%2Fadmin/suggest?q=printer")]
    public async Task A_key_that_is_not_a_slug_is_an_empty_list_and_no_call(string path)
    {
        await using var factory = Host();

        var (response, body) = await GetAsync(factory, path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetArrayLength().ShouldBe(0);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_apis_429_is_passed_through_as_a_429_with_an_empty_list()
    {
        await using var factory = Host();
        factory.Api.OnStatus(HttpMethod.Get, Api, HttpStatusCode.TooManyRequests);

        var (response, body) = await GetAsync(factory, "/p/paperplane/suggest?q=printer");

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        body.GetArrayLength().ShouldBe(0);
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Any_other_failure_is_an_empty_list(HttpStatusCode status)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Get, Api, status, "x", "Npgsql host=10.0.0.5");

        var (response, body) = await GetAsync(factory, "/p/paperplane/suggest?q=printer");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_transport_failure_is_an_empty_list_too()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Get, Api, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var (response, body) = await GetAsync(factory, "/p/paperplane/suggest?q=printer");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_response_is_no_store_noindex_and_has_the_shared_security_headers()
    {
        await using var factory = Host();

        var (response, _) = await GetAsync(factory, "/p/paperplane/suggest?q=printer");

        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
    }

    [Fact]
    public async Task Only_a_get_is_answered()
    {
        await using var factory = Host();
        using var client = FormTestKit.Client(factory);

        using var post = await client.PostAsync("/p/paperplane/suggest?q=printer", new StringContent(string.Empty), Ct);

        post.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task It_is_not_a_kb_category_and_the_kb_path_does_not_answer_it()
    {
        await using var factory = Host();

        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/paperplane/kb/suggest?q=printer", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, "the adapter is /p/{key}/suggest, beside the kb and not under it");
        response.Content.Headers.ContentType!.MediaType.ShouldNotBe("application/json");
    }
}
