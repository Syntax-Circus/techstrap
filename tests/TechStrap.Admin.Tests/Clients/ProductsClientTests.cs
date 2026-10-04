using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class ProductsClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid KeyId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static ProductDto Product(uint version = 3, bool isActive = true) => new(
        ProductId, "orbitly", "Orbitly", "ORB", isActive, new ProductBrandingDto("Orbitly", null, "#2563EB", "#FFFFFF", "#1E40AF", null, null), version);

    private static ProductApiKeyDto Key(string kind = ApiKeyKinds.Trusted) =>
        new(KeyId, ProductId, kind, "tsk_ab12", "Server", DateTimeOffset.UtcNow, null, null);

    private static JsonElement Body(ApiHarness api) => JsonDocument.Parse(api.Stub.Requests.Last().Body!).RootElement;

    [Fact]
    public async Task Get_reads_the_product_and_a_404_keeps_the_product_not_found_code()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{ProductId}", Product());

        (await api.Get<IProductsClient>().GetAsync(ProductId, Ct)).Value.Version.ShouldBe(3u);

        api.Stub.OnProblem(HttpMethod.Get, $"/api/products/{ProductId}", HttpStatusCode.NotFound, ApiErrorCodes.ProductNotFound, "No such product.");
        (await api.Get<IProductsClient>().GetAsync(ProductId, Ct)).Errors.ShouldHaveSingleItem()
            .ShouldBe(new ResultError(ApiErrorCodes.ProductNotFound, "No such product.", ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task Create_posts_the_request_and_returns_the_product()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Post, "/api/products", Product(), HttpStatusCode.Created);

        var result = await api.Get<IProductsClient>().CreateAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, "#2563EB", null, null)), Ct);

        result.Value.NumberPrefix.ShouldBe("ORB");
        var body = Body(api);
        body.GetProperty("key").GetString().ShouldBe("orbitly");
        body.GetProperty("numberPrefix").GetString().ShouldBe("ORB");
        body.GetProperty("branding").GetProperty("accentColour").GetString().ShouldBe("#2563EB");
    }

    // Review Focus 3: IsActive is not nullable in UpdateProductRequest, so it must always travel; a missing flag would deactivate the product. The version travels too.
    [Fact]
    public async Task Update_sends_the_version_and_the_active_flag_in_the_body()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Put, $"/api/products/{ProductId}", Product(version: 4));

        var result = await api.Get<IProductsClient>().UpdateAsync(
            ProductId, new UpdateProductRequest("Orbitly Cloud", new ProductBrandingRequest("Orbitly", "https://cdn.orbitly.example/logo.png", "#2563EB", "help@orbitly.example", null), IsActive: true, Version: 3), Ct);

        result.Value.Version.ShouldBe(4u);
        var body = Body(api);
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        body.GetProperty("version").GetUInt32().ShouldBe(3u);
        body.GetProperty("name").GetString().ShouldBe("Orbitly Cloud");
        body.GetProperty("branding").GetProperty("logoPath").GetString().ShouldBe("https://cdn.orbitly.example/logo.png");
        api.Stub.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Put);
    }

    [Fact]
    public async Task Update_keeps_a_deactivation_as_false_and_maps_a_stale_version_to_a_conflict_result()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Put, $"/api/products/{ProductId}", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "This product changed since you opened it.");

        var result = await api.Get<IProductsClient>().UpdateAsync(
            ProductId, new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", null, null, null, null), IsActive: false, Version: 2), Ct);

        Body(api).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ConcurrencyConflict, "This product changed since you opened it.", ResultErrorKind.Conflict));
    }

    [Fact]
    public async Task A_400_on_update_maps_each_kebab_case_field_to_its_own_error()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.On(HttpMethod.Put, $"/api/products/{ProductId}", _ => StubApiHandler.ValidationProblem(
        [
            (ApiFields.LogoPath, ApiErrorCodes.LogoPathInvalid, "logo-path must be an https URL (or http for localhost)."),
            (ApiFields.AccentColour, "accent-colour-invalid", "accent-colour must be a #RRGGBB colour."),
        ]));

        var result = await api.Get<IProductsClient>().UpdateAsync(
            ProductId, new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", "javascript:alert(1)", "purple", null, null), true, 3), Ct);

        result.Errors.Select(e => (e.Target, e.Code)).ShouldBe(
            [(ApiFields.LogoPath, ApiErrorCodes.LogoPathInvalid), (ApiFields.AccentColour, "accent-colour-invalid")], ignoreOrder: true);
    }

    [Fact]
    public async Task A_duplicate_key_is_a_conflict_with_the_product_key_taken_code()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Post, "/api/products", HttpStatusCode.Conflict, ApiErrorCodes.ProductKeyTaken, "That key or number prefix is already used.");

        var result = await api.Get<IProductsClient>().CreateAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", null), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ProductKeyTaken);
    }

    [Fact]
    public async Task Api_keys_are_listed_created_and_revoked_on_the_product_routes()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub
            .OnJson(HttpMethod.Get, $"/api/products/{ProductId}/api-keys", new[] { Key(), Key(ApiKeyKinds.Public) })
            .OnJson(HttpMethod.Post, $"/api/products/{ProductId}/api-keys", new CreateProductApiKeyResponse(Key(ApiKeyKinds.Public), "tsk_the-plaintext-secret"), HttpStatusCode.Created)
            .OnStatus(HttpMethod.Delete, $"/api/products/{ProductId}/api-keys/{KeyId}", HttpStatusCode.NoContent);
        var client = api.Get<IProductsClient>();

        (await client.ListApiKeysAsync(ProductId, Ct)).Value.Select(k => k.Kind).ShouldBe([ApiKeyKinds.Trusted, ApiKeyKinds.Public]);
        var created = await client.CreateApiKeyAsync(ProductId, new CreateProductApiKeyRequest(ApiKeyKinds.Public, "Website"), Ct);
        (await client.RevokeApiKeyAsync(ProductId, KeyId, Ct)).IsSuccess.ShouldBeTrue();

        created.Value.PlaintextKey.ShouldBe("tsk_the-plaintext-secret");
        created.Value.Key.Kind.ShouldBe(ApiKeyKinds.Public);
        api.Stub.Requests.Select(r => (r.Method.Method, r.Path)).ShouldBe(
        [
            ("GET", $"/api/products/{ProductId}/api-keys"),
            ("POST", $"/api/products/{ProductId}/api-keys"),
            ("DELETE", $"/api/products/{ProductId}/api-keys/{KeyId}"),
        ]);
        var post = api.Stub.Requests[1];
        JsonDocument.Parse(post.Body!).RootElement.GetProperty("kind").GetString().ShouldBe("Public");
        JsonDocument.Parse(post.Body!).RootElement.GetProperty("label").GetString().ShouldBe("Website");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Creating_a_key_is_never_retried_and_an_unknown_outcome_is_an_uncertain_write(HttpStatusCode status)
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnStatus(HttpMethod.Post, $"/api/products/{ProductId}/api-keys", status);

        var result = await api.Get<IProductsClient>().CreateApiKeyAsync(ProductId, new CreateProductApiKeyRequest("Trusted", null), Ct);

        ApiErrorCodes.IsUncertainWrite(result.Errors[0].Code).ShouldBeTrue();
        api.Stub.Count(HttpMethod.Post, $"/api/products/{ProductId}/api-keys").ShouldBe(1);
    }

    [Fact]
    public async Task A_bad_kind_and_a_missing_key_keep_their_codes()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnValidationProblem(HttpMethod.Post, $"/api/products/{ProductId}/api-keys", ApiFields.Kind, ApiErrorCodes.ApiKeyKindInvalid, "Choose Trusted or Public.");
        api.Stub.OnProblem(HttpMethod.Delete, $"/api/products/{ProductId}/api-keys/{KeyId}", HttpStatusCode.NotFound, ApiErrorCodes.ApiKeyNotFound, "That API key does not exist for this product.");
        var client = api.Get<IProductsClient>();

        (await client.CreateApiKeyAsync(ProductId, new CreateProductApiKeyRequest("Other", null), Ct)).Errors.ShouldHaveSingleItem()
            .ShouldBe(new ResultError(ApiErrorCodes.ApiKeyKindInvalid, "Choose Trusted or Public.", ResultErrorKind.Validation, "kind"));
        (await client.RevokeApiKeyAsync(ProductId, KeyId, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiKeyNotFound);
    }

    [Fact]
    public async Task Every_product_call_carries_the_bearer_token()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{ProductId}", Product()).OnJson(HttpMethod.Put, $"/api/products/{ProductId}", Product());
        var client = api.Get<IProductsClient>();

        await client.GetAsync(ProductId, Ct);
        await client.UpdateAsync(ProductId, new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", null, null, null, null), true, 3), Ct);

        api.Stub.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }
}
