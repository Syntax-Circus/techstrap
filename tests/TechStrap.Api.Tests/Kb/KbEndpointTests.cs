using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TechStrap.Api.Startup;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Tests.Kb;

/// <summary>The knowledge-base API over the real host and a real database: policies, the write and read flow, the preview, image upload and the public cache headers.</summary>
public sealed class KbEndpointTests(TestPostgres postgres) : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, .. new byte[40]];

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-kbapi-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, recursive: true);
        }
    }

    private sealed record Started(ApiFactory Factory, HttpClient Admin, HttpClient Agent, HttpClient Anonymous) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            Agent.Dispose();
            Anonymous.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private async Task<Started> StartAsync(IReadOnlyDictionary<string, string?>? extra = null, bool kestrel = false)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["Storage:Local:RootPath"] = _storage };
        foreach (var pair in extra ?? new Dictionary<string, string?>())
        {
            settings[pair.Key] = pair.Value;
        }

        var factory = new ApiFactory(settings: settings);
        if (kestrel)
        {
            factory.UseKestrel(0); // TestServer has no IHttpMaxRequestBodySizeFeature, so RequestSizeLimit needs the real server.
        }

        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        (await admin.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        (await agent.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        return new Started(factory, admin, agent, factory.CreateClient());
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, string key, string prefix)
    {
        using var response = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(key, key, prefix, null), Ct);
        return await ReadAsync<ProductDto>(response);
    }

    private static async Task<KbCategoryDto> CreateCategoryAsync(HttpClient agent, Guid? productId, string slug)
    {
        using var response = await agent.PostAsJsonAsync("/api/kb/categories", new CreateKbCategoryRequest(productId, slug, slug, null, 1), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadAsync<KbCategoryDto>(response);
    }

    private static async Task<KbArticleDto> CreateArticleAsync(HttpClient agent, Guid? productId, Guid? categoryId, string slug, string body = "Body")
    {
        using var response = await agent.PostAsJsonAsync("/api/kb/articles", new CreateKbArticleRequest(productId, categoryId, slug, "Title " + slug, "Summary of " + slug, body), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadAsync<KbArticleDto>(response);
    }

    [Theory]
    [InlineData("GET", "/api/kb/articles")]
    [InlineData("GET", "/api/kb/articles/0199a000-0000-7000-8000-000000000001")]
    [InlineData("POST", "/api/kb/articles")]
    [InlineData("PUT", "/api/kb/articles/0199a000-0000-7000-8000-000000000001")]
    [InlineData("POST", "/api/kb/articles/0199a000-0000-7000-8000-000000000001/publish")]
    [InlineData("POST", "/api/kb/articles/0199a000-0000-7000-8000-000000000001/archive")]
    [InlineData("POST", "/api/kb/preview")]
    [InlineData("POST", "/api/kb/images")]
    [InlineData("GET", "/api/kb/categories")]
    [InlineData("POST", "/api/kb/categories")]
    [InlineData("PUT", "/api/kb/categories/0199a000-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/kb/categories/0199a000-0000-7000-8000-000000000001")]
    public async Task Every_agent_route_refuses_an_anonymous_caller_and_a_signed_in_user_who_is_not_an_agent(string method, string path)
    {
        await using var factory = new ApiFactory();
        using var anonymous = factory.CreateClient();
        using var outsider = factory.CreateClient().Bearer(TestJwt.Token("nobody", ["some-other-group"], email: "nobody@example.com"));

        using var withoutToken = await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);
        using var withoutGroup = await outsider.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        withoutToken.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        withoutGroup.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_cannot_delete_a_category_but_an_admin_can_once_it_is_empty()
    {
        await using var started = await StartAsync();
        var category = await CreateCategoryAsync(started.Agent, null, "general");
        var article = await CreateArticleAsync(started.Agent, null, category.Id, "welcome");

        using var byAgent = await started.Agent.DeleteAsync($"/api/kb/categories/{category.Id}", Ct);
        using var blocked = await started.Admin.DeleteAsync($"/api/kb/categories/{category.Id}", Ct);
        using var moved = await started.Agent.PutAsJsonAsync($"/api/kb/articles/{article.Id}", new UpdateKbArticleRequest(null, article.Title, null, "Body", article.Version), Ct);
        using var deleted = await started.Admin.DeleteAsync($"/api/kb/categories/{category.Id}", Ct);
        using var gone = await started.Admin.DeleteAsync($"/api/kb/categories/{category.Id}", Ct);

        byAgent.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-category-in-use");
        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await gone.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-category-not-found");
    }

    [Fact]
    public async Task An_article_written_by_an_agent_reaches_the_public_api_only_while_it_is_published()
    {
        await using var started = await StartAsync();
        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
        var category = await CreateCategoryAsync(started.Agent, product.Id, "account");
        var draft = await CreateArticleAsync(started.Agent, product.Id, category.Id, "reset-password", "# Reset\n\nOpen the app.");
        draft.Status.ShouldBe("Draft");
        var search = $"/api/public/kb/orbitly/search?q=reset";
        var article = "/api/public/kb/orbitly/articles/account/reset-password";

        (await ReadAsync<PagedResponse<PublicKbSearchResultDto>>(await started.Anonymous.GetAsync(search, Ct))).Items.ShouldBeEmpty();
        using var hiddenDraft = await started.Anonymous.GetAsync(article, Ct);
        hiddenDraft.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        hiddenDraft.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        (await hiddenDraft.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-article-not-found");

        using var publishResponse = await started.Agent.PostAsync($"/api/kb/articles/{draft.Id}/publish?version={draft.Version}", null, Ct);
        var published = await ReadAsync<KbArticleDto>(publishResponse);
        published.ShouldSatisfyAllConditions(dto => dto.Status.ShouldBe("Published"), dto => dto.PublishedAt.ShouldNotBeNull(), dto => dto.Version.ShouldNotBe(draft.Version));

        using var searchResponse = await started.Anonymous.GetAsync(search, Ct);
        var hits = await ReadAsync<PagedResponse<PublicKbSearchResultDto>>(searchResponse);
        hits.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            hit => hit.Slug.ShouldBe("reset-password"),
            hit => hit.CategorySlug.ShouldBe("account"),
            hit => hit.ProductKey.ShouldBe("orbitly"),
            hit => hit.Snippet.ShouldBe("Summary of reset-password"));
        searchResponse.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=60");
        using var articleResponse = await started.Anonymous.GetAsync(article, Ct);
        var page = await ReadAsync<PublishedKbArticleDto>(articleResponse);
        page.Html.ShouldBe("<h1>Reset</h1>\n<p>Open the app.</p>\n");
        page.Title.ShouldBe("Title reset-password");
        articleResponse.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=60");
        (await articleResponse.Content.ReadAsStringAsync(Ct)).ShouldNotContain("authorAgentId");
        using var categories = await started.Anonymous.GetAsync("/api/public/kb/orbitly/categories", Ct);
        (await ReadAsync<List<PublicKbCategoryDto>>(categories)).ShouldBe([new PublicKbCategoryDto("account", "account", null, 1)]);
        using var sitemap = await started.Anonymous.GetAsync("/api/public/kb/orbitly/sitemap", Ct);
        (await ReadAsync<List<KbSitemapEntryDto>>(sitemap)).ShouldHaveSingleItem().Slug.ShouldBe("reset-password");
        sitemap.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=300");

        using var archive = await started.Agent.PostAsync($"/api/kb/articles/{published.Id}/archive", null, Ct);
        (await ReadAsync<KbArticleDto>(archive)).Status.ShouldBe("Archived");
        (await ReadAsync<PagedResponse<PublicKbSearchResultDto>>(await started.Anonymous.GetAsync(search, Ct))).Items.ShouldBeEmpty();
        (await started.Anonymous.GetAsync(article, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync<List<KbSitemapEntryDto>>(await started.Anonymous.GetAsync("/api/public/kb/orbitly/sitemap", Ct))).ShouldBeEmpty();
        (await ReadAsync<List<PublicKbCategoryDto>>(await started.Anonymous.GetAsync("/api/public/kb/orbitly/categories", Ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_product_gets_empty_lists_and_one_uniform_not_found_for_the_article()
    {
        await using var started = await StartAsync();

        (await ReadAsync<PagedResponse<PublicKbSearchResultDto>>(await started.Anonymous.GetAsync("/api/public/kb/nobody/search?q=anything", Ct))).Items.ShouldBeEmpty();
        (await ReadAsync<List<PublicKbCategoryDto>>(await started.Anonymous.GetAsync("/api/public/kb/nobody/categories", Ct))).ShouldBeEmpty();
        (await ReadAsync<List<KbSitemapEntryDto>>(await started.Anonymous.GetAsync("/api/public/kb/nobody/sitemap", Ct))).ShouldBeEmpty();
        using var article = await started.Anonymous.GetAsync("/api/public/kb/nobody/articles/a/b", Ct);
        article.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_slug_is_refused_across_scopes_and_a_stale_version_is_a_conflict()
    {
        await using var started = await StartAsync();
        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
        var shared = await CreateCategoryAsync(started.Agent, null, "general");
        var welcome = await CreateArticleAsync(started.Agent, null, shared.Id, "welcome");

        using var reuse = await started.Agent.PostAsJsonAsync("/api/kb/articles", new CreateKbArticleRequest(product.Id, shared.Id, "welcome", "Welcome again", null, "Body"), Ct);
        using var firstEdit = await started.Agent.PutAsJsonAsync($"/api/kb/articles/{welcome.Id}", new UpdateKbArticleRequest(shared.Id, "First", null, "Body", welcome.Version), Ct);
        using var secondEdit = await started.Agent.PutAsJsonAsync($"/api/kb/articles/{welcome.Id}", new UpdateKbArticleRequest(shared.Id, "Second", null, "Body", welcome.Version), Ct);
        using var staleCategory = await started.Agent.PutAsJsonAsync($"/api/kb/categories/{shared.Id}", new UpdateKbCategoryRequest("Renamed", null, 1, shared.Version + 100), Ct);

        reuse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await reuse.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-slug-taken");
        firstEdit.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondEdit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await secondEdit.Content.ReadAsStringAsync(Ct)).ShouldContain("concurrency-conflict");
        staleCategory.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await started.Agent.GetFromJsonAsync<KbArticleDto>($"/api/kb/articles/{welcome.Id}", Ct))!.Title.ShouldBe("First");
    }

    [Fact]
    public async Task The_list_filters_by_status_scope_and_text_and_publish_without_a_category_is_a_field_error()
    {
        await using var started = await StartAsync();
        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
        var category = await CreateCategoryAsync(started.Agent, null, "general");
        var shared = await CreateArticleAsync(started.Agent, null, category.Id, "shared-one", "printer text");
        var own = await CreateArticleAsync(started.Agent, product.Id, null, "own-one", "scanner text");

        var all = await started.Agent.GetFromJsonAsync<PagedResponse<KbArticleListItemDto>>("/api/kb/articles", Ct);
        var sharedOnly = await started.Agent.GetFromJsonAsync<PagedResponse<KbArticleListItemDto>>("/api/kb/articles?sharedOnly=true", Ct);
        var ownOnly = await started.Agent.GetFromJsonAsync<PagedResponse<KbArticleListItemDto>>($"/api/kb/articles?productId={product.Id}&includeShared=false", Ct);
        var searched = await started.Agent.GetFromJsonAsync<PagedResponse<KbArticleListItemDto>>("/api/kb/articles?text=scanner", Ct);
        using var badStatus = await started.Agent.GetAsync("/api/kb/articles?status=Live", Ct);
        using var incomplete = await started.Agent.PostAsync($"/api/kb/articles/{own.Id}/publish", null, Ct);

        all!.Items.Select(item => item.Slug).Order().ShouldBe(["own-one", "shared-one"]);
        sharedOnly!.Items.ShouldHaveSingleItem().Id.ShouldBe(shared.Id);
        ownOnly!.Items.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
        searched!.Items.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
        badStatus.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        incomplete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await incomplete.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("kb-publish-incomplete");
        body.ShouldContain("category");
    }

    [Fact]
    public async Task The_preview_returns_sanitised_html_and_refuses_hostile_markup_an_oversize_source_and_non_agents()
    {
        await using var started = await StartAsync();
        const string Hostile = "<script>alert(1)</script> [x](javascript:alert(1)) ![p](javascript:alert(2)) ![q](//evil.example/a.png) <img src=x onerror=alert(3)>\n\n| a |\n|---|\n| <b onclick=x>1</b> |\n\n![ok](https://cdn.example.com/a.png)";

        using var response = await started.Agent.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest(Hostile), Ct);
        var html = (await ReadAsync<KbPreviewResponse>(response)).Html;
        using var tooLong = await started.Agent.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest(new string('a', KbLimits.MaxPreviewChars + 1)), Ct);
        using var anonymous = await started.Anonymous.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest("# hi"), Ct);

        html.ShouldNotContain("<script");
        html.ShouldNotContain("href=\"javascript");
        html.ShouldNotContain("src=\"javascript");
        html.ShouldNotContain("//evil.example");
        html.ShouldNotContain("onerror=\"");
        html.ShouldNotContain("onclick=\"");
        html.ShouldNotContain("<img src=x");
        html.ShouldContain("<table>");
        html.ShouldContain("<img src=\"https://cdn.example.com/a.png\" alt=\"ok\"");
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync(Ct)).ShouldContain("body-too-long");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_preview_of_an_article_equals_its_public_page()
    {
        await using var started = await StartAsync();
        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
        var category = await CreateCategoryAsync(started.Agent, product.Id, "account");
        const string Markdown = "# Title\n\n- one\n- two\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n![pic](https://cdn.example.com/a.png)";
        var article = await CreateArticleAsync(started.Agent, product.Id, category.Id, "same", Markdown);
        (await started.Agent.PostAsync($"/api/kb/articles/{article.Id}/publish", null, Ct)).EnsureSuccessStatusCode();

        var preview = await ReadAsync<KbPreviewResponse>(await started.Agent.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest(Markdown), Ct));
        var page = await ReadAsync<PublishedKbArticleDto>(await started.Anonymous.GetAsync("/api/public/kb/orbitly/articles/account/same", Ct));

        page.Html.ShouldBe(preview.Html);
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName = "photo.png", string contentType = "image/png", string field = "file")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, field, fileName } };
    }

    [Fact]
    public async Task An_uploaded_image_returns_its_key_and_public_url_and_is_then_served_anonymously()
    {
        await using var started = await StartAsync();

        using var response = await started.Agent.PostAsync("/api/kb/images", Form(Png), Ct);
        var uploaded = await ReadAsync<KbImageUploadResponse>(response);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        uploaded.Key.ShouldStartWith("kb-images/");
        uploaded.Key.ShouldEndWith(".png");
        uploaded.Key.ShouldNotContain("photo");
        uploaded.Url.ShouldBe("https://api.test/" + uploaded.Key);
        using var served = await started.Anonymous.GetAsync("/" + uploaded.Key, Ct);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await served.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
        served.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        served.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("sandbox");
    }

    public static TheoryData<string, byte[], string, string> RefusedUploads() => new()
    {
        { "svg named png", "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>"u8.ToArray(), "image/png", "photo.png" },
        { "html named png", "<html><script>alert(1)</script></html>"u8.ToArray(), "image/png", "photo.png" },
        { "png named svg with an svg type", Png, "image/svg+xml", "photo.svg" },
        { "gif that is a page", [.. "GIF89a"u8.ToArray(), .. new byte[8], .. "<script>alert(1)</script>"u8.ToArray()], "image/gif", "photo.gif" },
        { "text", "hello"u8.ToArray(), "text/plain", "notes.txt" },
    };

    [Theory]
    [MemberData(nameof(RefusedUploads))]
    public async Task A_polyglot_svg_or_mislabelled_upload_is_refused_and_nothing_is_stored(string label, byte[] bytes, string contentType, string fileName)
    {
        await using var started = await StartAsync();

        using var response = await started.Agent.PostAsync("/api/kb/images", Form(bytes, fileName, contentType), Ct);

        // The png named .svg with an svg content type is a real png: the bytes decide, so it is stored under a random png name (the label says so).
        if (label.StartsWith("png named svg", StringComparison.Ordinal))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created, label);
            (await ReadAsync<KbImageUploadResponse>(response)).Key.ShouldEndWith(".png");
            return;
        }

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, label);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-image-type-not-allowed");
        Directory.Exists(Path.Combine(_storage, "kb-images")).ShouldBeFalse(label);
    }

    [Fact]
    public async Task A_missing_file_a_wrong_field_a_wrong_content_type_and_oversize_bodies_are_refused_with_their_own_statuses()
    {
        await using var started = await StartAsync();
        var overLimit = new byte[KbLimits.MaxImageBytes + 1024];
        Png.CopyTo(overLimit, 0);

        using var noFile = await started.Agent.PostAsync("/api/kb/images", new MultipartFormDataContent { { new StringContent("x"), "other" } }, Ct);
        using var wrongField = await started.Agent.PostAsync("/api/kb/images", Form(Png, field: "image"), Ct);
        using var json = await started.Agent.PostAsJsonAsync("/api/kb/images", new { file = "x" }, Ct);
        using var tooLarge = await started.Agent.PostAsync("/api/kb/images", Form(overLimit), Ct);
        using var anonymous = await started.Anonymous.PostAsync("/api/kb/images", Form(Png), Ct);

        noFile.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noFile.Content.ReadAsStringAsync(Ct)).ShouldContain("file-required");
        wrongField.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrongField.Content.ReadAsStringAsync(Ct)).ShouldContain("file-required");
        json.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLarge.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-image-too-large");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        Directory.Exists(Path.Combine(_storage, "kb-images")).ShouldBeFalse();
    }

    [Fact]
    public async Task An_image_body_far_over_the_limit_is_stopped_by_the_server_as_413_problem_details_and_nothing_is_stored()
    {
        await using var started = await StartAsync(kestrel: true);
        var farOver = new byte[KbRequestLimits.ImageFormBytes + 1];
        Png.CopyTo(farOver, 0);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/kb/images") { Content = Form(farOver) };

        // Expect: 100-continue lets Kestrel reject on Content-Length before the client streams the whole body into a closing socket.
        request.Headers.ExpectContinue = true;
        using var response = await started.Agent.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("request-too-large");
        Directory.Exists(Path.Combine(_storage, "kb-images")).ShouldBeFalse();
    }

    [Fact]
    public async Task The_preview_answers_json_never_a_page_and_an_oversize_json_body_is_a_413()
    {
        await using var started = await StartAsync(kestrel: true);

        using var ok = await started.Agent.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest("<script>alert(1)</script>"), Ct);
        using var huge = new HttpRequestMessage(HttpMethod.Post, "/api/kb/preview") { Content = JsonContent.Create(new KbPreviewRequest(new string('a', KbRequestLimits.JsonBodyBytes + 10))) };
        huge.Headers.ExpectContinue = true;
        using var tooBig = await started.Agent.SendAsync(huge, Ct);

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        ok.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        tooBig.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await tooBig.Content.ReadAsStringAsync(Ct)).ShouldContain("request-too-large");
    }

    [Fact]
    public async Task The_client_file_name_and_content_type_never_reach_the_stored_key()
    {
        await using var started = await StartAsync();

        using var response = await started.Agent.PostAsync("/api/kb/images", Form(Png, "../../evil<script>.html", "text/html"), Ct);
        var uploaded = await ReadAsync<KbImageUploadResponse>(response);

        uploaded.Key.ShouldStartWith("kb-images/");
        uploaded.Key.ShouldEndWith(".png");
        uploaded.Key.ShouldNotContain("evil");
        uploaded.Key.ShouldNotContain("..");
    }

    [Fact]
    public async Task Agent_kb_responses_are_no_store_while_the_public_page_and_the_image_keep_their_caches()
    {
        await using var started = await StartAsync();
        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
        var category = await CreateCategoryAsync(started.Agent, product.Id, "account");
        var article = await CreateArticleAsync(started.Agent, product.Id, category.Id, "cached", "Body");
        (await started.Agent.PostAsync($"/api/kb/articles/{article.Id}/publish", null, Ct)).EnsureSuccessStatusCode();
        using var image = await started.Agent.PostAsync("/api/kb/images", Form(Png), Ct);
        var uploaded = await ReadAsync<KbImageUploadResponse>(image);

        using var agentGet = await started.Agent.GetAsync($"/api/kb/articles/{article.Id}", Ct);
        using var agentList = await started.Agent.GetAsync("/api/kb/articles", Ct);
        using var agentCreate = await started.Agent.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest("x"), Ct);
        using var publicPage = await started.Anonymous.GetAsync("/api/public/kb/orbitly/articles/account/cached", Ct);
        using var served = await started.Anonymous.GetAsync("/" + uploaded.Key, Ct);

        agentGet.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        agentList.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        agentCreate.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        image.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        publicPage.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=60");
        served.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=31536000, immutable");
    }

    [Theory]
    [InlineData("publish", "abc")]
    [InlineData("publish", "-1")]
    [InlineData("archive", "abc")]
    [InlineData("archive", "99999999999")]
    public async Task A_version_query_that_is_not_an_unsigned_number_is_a_400(string action, string version)
    {
        await using var started = await StartAsync();
        var article = await CreateArticleAsync(started.Agent, null, null, "versioned");

        using var response = await started.Agent.PostAsync($"/api/kb/articles/{article.Id}/{action}?version={version}", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Archiving_or_publishing_with_a_stale_version_is_a_conflict()
    {
        await using var started = await StartAsync();
        var category = await CreateCategoryAsync(started.Agent, null, "general");
        var article = await CreateArticleAsync(started.Agent, null, category.Id, "stale");

        using var stalePublish = await started.Agent.PostAsync($"/api/kb/articles/{article.Id}/publish?version={article.Version + 100}", null, Ct);
        using var staleArchive = await started.Agent.PostAsync($"/api/kb/articles/{article.Id}/archive?version={article.Version + 100}", null, Ct);

        stalePublish.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        staleArchive.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await staleArchive.Content.ReadAsStringAsync(Ct)).ShouldContain("concurrency-conflict");
    }

    [Fact]
    public async Task A_maximum_length_non_ascii_article_is_accepted_not_refused_as_too_large()
    {
        await using var started = await StartAsync(kestrel: true);
        var body = new string('中', 200_000); // 6 bytes each once the default encoder escapes it

        using var response = await started.Agent.PostAsJsonAsync("/api/kb/articles", new CreateKbArticleRequest(null, null, "cjk", "Title", null, body), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_public_kb_routes_are_rate_limited_per_ip()
    {
        await using var started = await StartAsync(new Dictionary<string, string?> { ["RateLimiting:Public:PermitLimit"] = "3" });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await started.Anonymous.GetAsync("/api/public/kb/nobody/sitemap", Ct);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(3).ShouldAllBe(status => status == HttpStatusCode.OK);
        statuses.Skip(3).ShouldAllBe(status => status == HttpStatusCode.TooManyRequests);
    }
}
