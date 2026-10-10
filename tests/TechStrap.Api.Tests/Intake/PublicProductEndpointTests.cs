using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Tests.Intake;

public sealed class PublicProductEndpointTests(TestPostgres postgres) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, IntakeSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var seed = await IntakeTestData.SeedAsync(factory.Services, TestContext.Current.CancellationToken);
        return (factory, database, seed);
    }

    [Fact]
    public async Task An_anonymous_get_returns_200_with_public_caching()
    {
        // Arrange
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/api/public/products/orbitly", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.Public.ShouldBeTrue();
        response.Headers.CacheControl.MaxAge.ShouldBe(TimeSpan.FromSeconds(300));

        var body = (await response.Content.ReadFromJsonAsync<PublicProductDto>(cancellationToken: TestContext.Current.CancellationToken))!;
        body.Key.ShouldBe("orbitly");
    }

    [Fact]
    public async Task The_anonymous_list_names_the_active_products_by_key_display_name_and_portal_host_only_with_public_caching()
    {
        // Arrange
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/api/public/products", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.Public.ShouldBeTrue();
        response.Headers.CacheControl.MaxAge.ShouldBe(TimeSpan.FromSeconds(300));
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var list = System.Text.Json.JsonSerializer.Deserialize<List<PublicProductSummaryDto>>(raw, System.Text.Json.JsonSerializerOptions.Web)!;
        list.ShouldBe([new PublicProductSummaryDto("orbitly", "Orbitly", AccentColour: "#1F6FEB"), new PublicProductSummaryDto("paperplane", "Paperplane", AccentColour: "#1F6FEB")]);
        raw.ShouldNotContain("dormant");
        raw.ShouldNotContain("logoPath");
        System.Text.Json.JsonDocument.Parse(raw).RootElement[0].EnumerateObject().Select(property => property.Name).ShouldBe(["key", "displayName", "portalHost", "tagline", "logoUrl", "accentColour", "listedOnLanding"]);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("dormant")]
    public async Task An_unknown_or_deactivated_product_is_404_no_store(string key)
    {
        // Arrange
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/public/products/{key}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }
}
