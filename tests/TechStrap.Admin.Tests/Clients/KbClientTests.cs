using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>The knowledge base client over the real handler pipeline and a stub API: the routes, the bodies, the multipart upload and the rule that a write is never retried.</summary>
public sealed class KbClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static KbArticleDto Article(uint version = 3, string status = KbArticleStatuses.Draft) =>
        new(ArticleId, ProductId, CategoryId, "reset-password", "Reset your password", "How to reset it.", "# Steps", status, Guid.NewGuid(), Now, Now, null, version);

    private static KbCategoryDto Category(uint version = 1) => new(CategoryId, null, "getting-started", "Getting started", null, 10, version);

    private static async Task<(ApiHarness Api, IKbClient Client)> StartAsync()
    {
        var api = await ApiHarness.CreateAsync();
        return (api, api.Get<IKbClient>());
    }

    private static JsonElement Body(ApiHarness api) => JsonDocument.Parse(api.Stub.Requests.Last().Body!).RootElement;

    [Fact]
    public async Task List_sends_the_filters_and_the_paging_and_leaves_blank_filters_out()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/kb/articles", new PagedResponse<KbArticleListItemDto>([], 2, 25, 0));

        var result = await client.ListAsync(new ListKbArticlesRequest(ProductId, SharedOnly: false, IncludeShared: true, KbArticleStatuses.Published, CategoryId, "reset password", 2, 25), Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe($"?productId={ProductId}&includeShared=true&status=Published&categoryId={CategoryId}&text=reset%20password&page=2&pageSize=25");

        await client.ListAsync(new ListKbArticlesRequest(null, SharedOnly: true, IncludeShared: false, null, null, " ", 1, 10), Ct);
        api.Stub.Requests.Last().Query.ShouldBe("?sharedOnly=true&page=1&pageSize=10");

        // The API defaults includeShared to true, so a product filter says false out loud: it means that product's own articles.
        await client.ListAsync(new ListKbArticlesRequest(ProductId, SharedOnly: false, IncludeShared: false, null, null, null, 1, 25), Ct);
        api.Stub.Requests.Last().Query.ShouldBe($"?productId={ProductId}&includeShared=false&page=1&pageSize=25");
    }

    [Fact]
    public async Task Get_returns_the_article_with_its_version_and_a_404_keeps_the_article_not_found_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", Article(version: 7));

        (await client.GetAsync(ArticleId, Ct)).Value.Version.ShouldBe(7u);

        api.Stub.OnProblem(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", HttpStatusCode.NotFound, ApiErrorCodes.KbArticleNotFound, "No such article.");
        (await client.GetAsync(ArticleId, Ct)).Errors.ShouldHaveSingleItem()
            .ShouldBe(new ResultError(ApiErrorCodes.KbArticleNotFound, "No such article.", ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task Create_posts_the_request_and_a_taken_slug_is_a_conflict_with_its_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/articles", Article(), HttpStatusCode.Created);

        var created = await client.CreateAsync(new CreateKbArticleRequest(ProductId, CategoryId, "reset-password", "Reset your password", "How to reset it.", "# Steps"), Ct);

        created.Value.Slug.ShouldBe("reset-password");
        var body = Body(api);
        body.GetProperty("productId").GetGuid().ShouldBe(ProductId);
        body.GetProperty("slug").GetString().ShouldBe("reset-password");
        body.GetProperty("bodyMarkdown").GetString().ShouldBe("# Steps");

        api.Stub.OnProblem(HttpMethod.Post, "/api/kb/articles", HttpStatusCode.Conflict, ApiErrorCodes.KbSlugTaken, "That slug is taken.");
        (await client.CreateAsync(new CreateKbArticleRequest(null, null, "reset-password", "T", "S", "B"), Ct)).Errors[0].Code.ShouldBe(ApiErrorCodes.KbSlugTaken);
    }

    // Review Focus 4: the version the article was loaded with always travels, so a stale save is a 409 and never an overwrite.
    [Fact]
    public async Task Update_sends_the_version_and_a_stale_version_is_a_conflict_that_is_not_retried()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Put, $"/api/kb/articles/{ArticleId}", Article(version: 4));

        var saved = await client.UpdateAsync(ArticleId, new UpdateKbArticleRequest(CategoryId, "Reset your password", "How to reset it.", "# Steps", Version: 3), Ct);

        saved.Value.Version.ShouldBe(4u);
        Body(api).GetProperty("version").GetUInt32().ShouldBe(3u);
        Body(api).GetProperty("title").GetString().ShouldBe("Reset your password");

        api.Stub.OnProblem(HttpMethod.Put, $"/api/kb/articles/{ArticleId}", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "This article changed since you opened it.");
        var stale = await client.UpdateAsync(ArticleId, new UpdateKbArticleRequest(CategoryId, "T", "S", "B", Version: 3), Ct);

        WriteOutcomes.Classify(stale.Errors[0]).ShouldBe(WriteOutcome.Conflict);
        api.Stub.Count(HttpMethod.Put, $"/api/kb/articles/{ArticleId}").ShouldBe(2);
    }

    [Fact]
    public async Task Publish_and_archive_post_to_their_routes_with_no_body_and_an_incomplete_publish_names_its_field()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", Article(version: 4, status: KbArticleStatuses.Published));
        api.Stub.OnJson(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/archive", Article(version: 5, status: KbArticleStatuses.Archived));

        (await client.PublishAsync(ArticleId, 3, Ct)).Value.Status.ShouldBe(KbArticleStatuses.Published);
        (await client.ArchiveAsync(ArticleId, 4, Ct)).Value.Version.ShouldBe(5u);

        // Review Focus 4: the version the article was loaded with travels in the query string, so a stale publish is a 409.
        api.Stub.Requests.Select(r => (r.Method, r.Path, r.Query, r.Body)).ShouldBe(
        [
            (HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", "?version=3", (string?)null),
            (HttpMethod.Post, $"/api/kb/articles/{ArticleId}/archive", "?version=4", null),
        ]);

        api.Stub.OnProblem(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "This article changed.");
        WriteOutcomes.Classify((await client.PublishAsync(ArticleId, 2, Ct)).Errors[0]).ShouldBe(WriteOutcome.Conflict);

        api.Stub.On(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", _ => StubApiHandler.ValidationProblem(ApiFields.Category, ApiErrorCodes.KbPublishIncomplete, "Choose a category before publishing."));
        var incomplete = await client.PublishAsync(ArticleId, 3, Ct);

        incomplete.Errors[0].Code.ShouldBe(ApiErrorCodes.KbPublishIncomplete);
        incomplete.Errors[0].Target.ShouldBe(ApiFields.Category);
    }

    [Fact]
    public async Task Preview_posts_the_markdown_to_the_preview_route_and_returns_the_html_unchanged()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/preview", new KbPreviewResponse("<p>Hi</p>"));

        var result = await client.PreviewAsync(new KbPreviewRequest("Hi"), Ct);

        result.Value.Html.ShouldBe("<p>Hi</p>");
        Body(api).GetProperty("bodyMarkdown").GetString().ShouldBe("Hi");
    }

    [Fact]
    public async Task A_superseded_preview_is_cancelled_by_its_caller_and_never_turned_into_a_result()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/preview", new KbPreviewResponse("<p>x</p>"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => client.PreviewAsync(new KbPreviewRequest("Hi"), cancelled.Token));
    }

    [Fact]
    public async Task Upload_sends_multipart_with_the_file_in_the_part_named_file_and_a_cleaned_file_name()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/images", new KbImageUploadResponse("kb-images/abc.png", "https://api.example/kb-images/abc.png"), HttpStatusCode.Created);

        var result = await client.UploadImageAsync(new KbImageFile("dir/\"shot\".png", "image/png", () => new MemoryStream([0x89, 0x50, 0x4E, 0x47])), Ct);

        result.Value.Url.ShouldBe("https://api.example/kb-images/abc.png");
        var request = api.Stub.Requests.ShouldHaveSingleItem();
        request.ContentType!.ShouldStartWith("multipart/form-data");
        var body = request.Body!;
        body.ShouldContain("name=file");
        body.ShouldContain("filename=dirshot.png");
        body.ShouldContain("Content-Type: image/png");
    }

    [Theory]
    [InlineData(ApiErrorCodes.KbImageTypeNotAllowed)]
    [InlineData(ApiErrorCodes.KbImageTooLarge)]
    public async Task An_image_the_api_refuses_keeps_its_code(string code)
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.On(HttpMethod.Post, "/api/kb/images", _ => StubApiHandler.ValidationProblem(ApiFields.File, code, "Not that image."));

        var result = await client.UploadImageAsync(new KbImageFile("a.png", "image/png", () => new MemoryStream([1])), Ct);

        result.Errors[0].Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_write_that_gets_an_unknown_answer_is_one_call_and_an_uncertain_write(HttpStatusCode status)
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnStatus(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", status);

        var result = await client.PublishAsync(ArticleId, 3, Ct);

        api.Stub.Count(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish").ShouldBe(1);
        ApiErrorCodes.IsUncertainWrite(result.Errors[0].Code).ShouldBeTrue();
    }

    [Fact]
    public async Task Categories_are_listed_created_updated_and_deleted_on_their_routes()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[Category()]);
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/categories", Category(), HttpStatusCode.Created);
        api.Stub.OnJson(HttpMethod.Put, $"/api/kb/categories/{CategoryId}", Category(version: 2));
        api.Stub.OnStatus(HttpMethod.Delete, $"/api/kb/categories/{CategoryId}", HttpStatusCode.NoContent);

        (await client.ListCategoriesAsync(Ct)).Value.ShouldHaveSingleItem().Slug.ShouldBe("getting-started");
        (await client.CreateCategoryAsync(new CreateKbCategoryRequest(null, "getting-started", "Getting started", null, 10), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("slug").GetString().ShouldBe("getting-started");

        (await client.UpdateCategoryAsync(CategoryId, new UpdateKbCategoryRequest("Getting started", null, 20, Version: 1), Ct)).Value.Version.ShouldBe(2u);
        Body(api).GetProperty("version").GetUInt32().ShouldBe(1u);
        Body(api).GetProperty("sortOrder").GetInt32().ShouldBe(20);

        (await client.DeleteCategoryAsync(CategoryId, Ct)).IsSuccess.ShouldBeTrue();
        api.Stub.Requests.Last().Method.ShouldBe(HttpMethod.Delete);
    }

    [Fact]
    public async Task A_category_that_still_holds_articles_is_a_conflict_with_the_in_use_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnProblem(HttpMethod.Delete, $"/api/kb/categories/{CategoryId}", HttpStatusCode.Conflict, ApiErrorCodes.KbCategoryInUse, "Move the 3 articles first.");

        var result = await client.DeleteCategoryAsync(CategoryId, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.KbCategoryInUse, "Move the 3 articles first.", ResultErrorKind.Conflict));
    }

    [Fact]
    public async Task Every_call_carries_the_agents_bearer_token()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[]);
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/preview", new KbPreviewResponse(string.Empty));

        await client.ListCategoriesAsync(Ct);
        await client.PreviewAsync(new KbPreviewRequest("x"), Ct);

        api.Stub.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }
}
