using System.Net.Http.Headers;
using SyntaxCircus.Common;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Clients;

/// <summary>
/// Products and their API keys. The reads work for any agent; every write and every key call is Admin only (the API enforces it with 403 admin-access-required).
/// Writes are never retried.
/// </summary>
public interface IProductsClient
{
    /// <summary>Active products for an Agent, all products for an Admin.</summary>
    Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/products/{id}</c>. 404 is product-not-found. The answer carries the <c>Version</c> the next update must send.</summary>
    Task<Result<ProductDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/products</c> (Admin, 201). 409 product-key-taken (the key or the number prefix is used); 400 fields (kebab-case): key, name, number-prefix,
    /// display-name, logo-path, accent-colour, from-address, reply-to.
    /// </summary>
    Task<Result<ProductDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>PUT /api/products/{id}</c> (Admin): sends <see cref="UpdateProductRequest.Version"/> and <see cref="UpdateProductRequest.IsActive"/>, which is not nullable, so a
    /// caller always passes the current value (an absent flag would deactivate the product). <see cref="UpdateProductRequest.PortalHost"/> null leaves the host unchanged; send an empty string to clear it. 409 concurrency-conflict when the version is stale. Returns the re-read product.
    /// </summary>
    Task<Result<ProductDto>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken);

    /// <summary><c>GET /api/products/{id}/api-keys</c> (Admin): newest first, revoked keys included, never a secret.</summary>
    Task<Result<IReadOnlyList<ProductApiKeyDto>>> ListApiKeysAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/products/{id}/api-keys</c> (Admin, 201). The answer holds <see cref="CreateProductApiKeyResponse.PlaintextKey"/> once. This is a write: it is never retried, and a lost
    /// answer leaves a key whose secret nobody saw (the page says to revoke it and create another). 400 api-key-kind-invalid (field kind) or label-too-long (field label).
    /// </summary>
    Task<Result<CreateProductApiKeyResponse>> CreateApiKeyAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/products/{id}/api-keys/{keyId}</c> (Admin, 204). Idempotent: revoking a revoked key succeeds. 404 api-key-not-found.</summary>
    Task<Result> RevokeApiKeyAsync(Guid productId, Guid keyId, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/products/{id}/logo</c> (Admin, multipart, part <c>file</c>): 400 product-logo-type-not-allowed, product-logo-too-large or file-required; 404 product-not-found;
    /// returns the re-read product with the new Version.
    /// </summary>
    Task<Result<ProductDto>> UploadLogoAsync(Guid productId, ProductLogoFile file, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/products/{id}/logo</c> (Admin): idempotent; returns the re-read product.</summary>
    Task<Result<ProductDto>> RemoveLogoAsync(Guid productId, CancellationToken cancellationToken);
}

/// <summary>A logo to upload. The stream is opened when the request is built and closed when it has been sent, so the same file can be sent again after a failure.</summary>
public sealed record ProductLogoFile(string FileName, string ContentType, Func<Stream> OpenRead);

/// <summary>Tags. <see cref="ListAsync"/> serves every agent (the tag picker and the queue filter); the summary and the writes are Admin only.</summary>
public interface ITagsClient
{
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/tags/summary</c> (Admin): every tag with the number of tickets that carry it, ordered by name (D-041).</summary>
    Task<Result<IReadOnlyList<TagSummaryDto>>> ListSummaryAsync(CancellationToken cancellationToken);

    /// <summary><c>POST /api/tags</c> (Admin, 201). 409 tag-slug-taken; 400 fields: slug, name, colour.</summary>
    Task<Result<TagDto>> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken);

    /// <summary><c>PUT /api/tags/{id}</c> (Admin). The slug is permanent. 404 tag-not-found; 400 fields: name, colour.</summary>
    Task<Result<TagDto>> UpdateAsync(Guid id, UpdateTagRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>DELETE /api/tags/{id}</c> (Admin, 204). With <paramref name="force"/> false a tag that is on tickets is a 409 tag-in-use whose message names the count; with true the tag is
    /// removed from every ticket first, and each of them records a TagRemoved event.
    /// </summary>
    Task<Result> DeleteAsync(Guid id, bool force, CancellationToken cancellationToken);
}

internal sealed class AgentsClient(ApiConnection connection) : IAgentsClient
{
    public const int PageSize = 100;

    // Stops a misbehaving API (a TotalCount that never converges) from looping for ever: 100 pages of 100 is 10 000 agents.
    private const int MaxPages = 100;

    public Task<Result<AgentDto>> GetMeAsync(CancellationToken cancellationToken) => connection.GetAsync<AgentDto>("api/agents/me", cancellationToken);

    public async Task<Result<IReadOnlyList<AgentListItemDto>>> ListAllAsync(CancellationToken cancellationToken)
    {
        var all = new List<AgentListItemDto>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await connection.GetAsync<PagedResponse<AgentListItemDto>>(ApiUri.Build("api/agents", ("page", page), ("pageSize", PageSize)), cancellationToken);
            if (result.IsFailure)
            {
                return Result<IReadOnlyList<AgentListItemDto>>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
            }

            all.AddRange(result.Value.Items);
            if (result.Value.Items.Count == 0 || all.Count >= result.Value.TotalCount)
            {
                break;
            }
        }

        return Result<IReadOnlyList<AgentListItemDto>>.Success(all);
    }

    public Task<Result<PagedResponse<AgentListItemDto>>> ListPageAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<AgentListItemDto>>(ApiUri.Build("api/agents", ("page", page), ("pageSize", pageSize)), cancellationToken);

    public Task<Result<AgentDto>> SetActiveAsync(Guid agentId, bool isActive, CancellationToken cancellationToken) =>
        connection.SendAsync<AgentDto>(HttpMethod.Put, $"api/agents/{agentId}", new UpdateAgentRequest(isActive), cancellationToken);

    public Task<Result> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Put, "api/agents/me/profile", request, cancellationToken);

    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> GetNotificationPreferencesAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<NotificationPreferenceDto>>("api/agents/me/notification-preferences", cancellationToken));

    public Task<Result> UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Put, "api/agents/me/notification-preferences", request, cancellationToken);
}

internal sealed class ProductsClient(ApiConnection connection) : IProductsClient
{
    public async Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken) =>
        Narrow(await connection.GetAsync<List<ProductDto>>("api/products", cancellationToken));

    public Task<Result<ProductDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        connection.GetAsync<ProductDto>($"api/products/{id}", cancellationToken);

    public Task<Result<ProductDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<ProductDto>(HttpMethod.Post, "api/products", request, cancellationToken);

    public Task<Result<ProductDto>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<ProductDto>(HttpMethod.Put, $"api/products/{id}", request, cancellationToken);

    public async Task<Result<IReadOnlyList<ProductApiKeyDto>>> ListApiKeysAsync(Guid productId, CancellationToken cancellationToken) =>
        Narrow(await connection.GetAsync<List<ProductApiKeyDto>>($"api/products/{productId}/api-keys", cancellationToken));

    public Task<Result<CreateProductApiKeyResponse>> CreateApiKeyAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<CreateProductApiKeyResponse>(HttpMethod.Post, $"api/products/{productId}/api-keys", request, cancellationToken);

    public Task<Result> RevokeApiKeyAsync(Guid productId, Guid keyId, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, $"api/products/{productId}/api-keys/{keyId}", null, cancellationToken);

    public async Task<Result<ProductDto>> UploadLogoAsync(Guid productId, ProductLogoFile file, CancellationToken cancellationToken)
    {
        Stream? stream = null;
        try
        {
            stream = file.OpenRead();
            using var form = new MultipartFormDataContent();
            var part = new StreamContent(stream);
            part.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.ContentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");

            // The browser's file name is cleaned first: an empty or odd name would make the multipart body throw.
            form.Add(part, ProductLogoLimits.FieldName, AttachmentFileName.Clean(file.FileName));
            return await connection.SendContentAsync<ProductDto>(HttpMethod.Post, $"api/products/{productId}/logo", form, cancellationToken);
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }
        }
    }

    public Task<Result<ProductDto>> RemoveLogoAsync(Guid productId, CancellationToken cancellationToken) =>
        connection.SendAsync<ProductDto>(HttpMethod.Delete, $"api/products/{productId}/logo", null, cancellationToken);

    internal static Result<IReadOnlyList<T>> Narrow<T>(Result<List<T>> result) =>
        result.IsSuccess ? Result<IReadOnlyList<T>>.Success(result.Value) : Result<IReadOnlyList<T>>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
}

internal sealed class TagsClient(ApiConnection connection) : ITagsClient
{
    public async Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<TagDto>>("api/tags", cancellationToken));

    public async Task<Result<IReadOnlyList<TagSummaryDto>>> ListSummaryAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<TagSummaryDto>>("api/tags/summary", cancellationToken));

    public Task<Result<TagDto>> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TagDto>(HttpMethod.Post, "api/tags", request, cancellationToken);

    public Task<Result<TagDto>> UpdateAsync(Guid id, UpdateTagRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TagDto>(HttpMethod.Put, $"api/tags/{id}", request, cancellationToken);

    public Task<Result> DeleteAsync(Guid id, bool force, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, ApiUri.Build($"api/tags/{id}", ("force", force ? "true" : null)), null, cancellationToken);
}
