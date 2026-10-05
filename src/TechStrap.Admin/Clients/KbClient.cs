using System.Net.Http.Headers;
using SyntaxCircus.Common;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Clients;

/// <summary>
/// A picture to upload. The stream is opened when the request is built and closed when it has been sent, so the same <see cref="KbImageFile"/> can be sent again after a failure
/// (the browser file stays selected until an upload succeeds).
/// </summary>
public sealed record KbImageFile(string FileName, string ContentType, Func<Stream> OpenRead);

/// <summary>
/// The knowledge base of the API: articles, the live preview, image upload and categories (PHASE-08). Reads work for every agent. A write is never retried: a stale
/// <c>Version</c> is a Conflict result with code <see cref="ApiErrorCodes.ConcurrencyConflict"/>, and a write whose answer was lost is one the caller treats as uncertain.
/// </summary>
public interface IKbClient
{
    /// <summary>
    /// <c>GET /api/kb/articles</c>: newest change first, or best match first for <see cref="ListKbArticlesRequest.Text"/>. Page and PageSize are always sent; <c>sharedOnly</c> only when true; <c>includeShared</c>
    /// (which the API defaults to true) is always sent with a product, so a product filter means exactly what the caller asked for; every other filter only when it is set. 400 status-invalid.
    /// </summary>
    Task<Result<PagedResponse<KbArticleListItemDto>>> ListAsync(ListKbArticlesRequest request, CancellationToken cancellationToken);

    /// <summary><c>GET /api/kb/articles/{id}</c>: the whole article with its Markdown and the <c>Version</c> the next update must send. 404 is kb-article-not-found.</summary>
    Task<Result<KbArticleDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/kb/articles</c> (201). 409 kb-slug-taken (the slug is used in this product or, across scopes, by a shared article or another product);
    /// 409 kb-category-scope-mismatch; 400 fields: title, slug, summary, body, category.
    /// </summary>
    Task<Result<KbArticleDto>> CreateAsync(CreateKbArticleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>PUT /api/kb/articles/{id}</c>. The product and the slug are permanent. 409 concurrency-conflict when the version is stale. Updating an archived article moves it back to Draft;
    /// the answer carries the status the article has now.
    /// </summary>
    Task<Result<KbArticleDto>> UpdateAsync(Guid id, UpdateKbArticleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/kb/articles/{id}/publish?version=N</c>: the version the article was loaded with travels, so publishing something another agent has changed is a 409 concurrency-conflict and not a
    /// publish of text nobody here has seen. Answers the article as it is now. 400 kb-publish-incomplete names the missing field (title, slug, body or category); 409 article-already-published.
    /// </summary>
    Task<Result<KbArticleDto>> PublishAsync(Guid id, uint version, CancellationToken cancellationToken);

    /// <summary><c>POST /api/kb/articles/{id}/archive?version=N</c>: the article leaves the portal. Version and answer as for publish. 409 article-already-archived.</summary>
    Task<Result<KbArticleDto>> ArchiveAsync(Guid id, uint version, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/kb/preview</c>: the Markdown rendered and sanitised by the same pipeline as the portal. A write call, so it is never retried; the editor cancels a call that a newer
    /// keystroke has replaced.
    /// </summary>
    Task<Result<KbPreviewResponse>> PreviewAsync(KbPreviewRequest request, CancellationToken cancellationToken);

    /// <summary><c>POST /api/kb/images</c> as multipart/form-data with the file in the part named <see cref="KbLimits.ImageFieldName"/> (<c>file</c>). 400 kb-image-type-not-allowed or kb-image-too-large.</summary>
    Task<Result<KbImageUploadResponse>> UploadImageAsync(KbImageFile file, CancellationToken cancellationToken);

    /// <summary><c>GET /api/kb/categories</c>: every category, shared ones included, by sort order.</summary>
    Task<Result<IReadOnlyList<KbCategoryDto>>> ListCategoriesAsync(CancellationToken cancellationToken);

    /// <summary><c>POST /api/kb/categories</c> (201). 409 kb-category-slug-taken; 400 kb-category-reserved-slug (the slug "search"); 400 fields: slug, name, description.</summary>
    Task<Result<KbCategoryDto>> CreateCategoryAsync(CreateKbCategoryRequest request, CancellationToken cancellationToken);

    /// <summary><c>PUT /api/kb/categories/{id}</c>. The product and the slug are permanent. 409 concurrency-conflict when the version is stale; 404 kb-category-not-found.</summary>
    Task<Result<KbCategoryDto>> UpdateCategoryAsync(Guid id, UpdateKbCategoryRequest request, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/kb/categories/{id}</c> (Admin, 204). 409 kb-category-in-use while articles are in it.</summary>
    Task<Result> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class KbClient(ApiConnection connection) : IKbClient
{
    public Task<Result<PagedResponse<KbArticleListItemDto>>> ListAsync(ListKbArticlesRequest request, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<KbArticleListItemDto>>(
            ApiUri.Build(
                "api/kb/articles",
                ("productId", request.ProductId),
                ("sharedOnly", request.SharedOnly ? "true" : null),
                ("includeShared", request.ProductId is null ? null : request.IncludeShared ? "true" : "false"),
                ("status", request.Status),
                ("categoryId", request.CategoryId),
                ("text", string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim()),
                ("page", request.Page),
                ("pageSize", request.PageSize)),
            cancellationToken);

    public Task<Result<KbArticleDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        connection.GetAsync<KbArticleDto>($"api/kb/articles/{id}", cancellationToken);

    public Task<Result<KbArticleDto>> CreateAsync(CreateKbArticleRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbArticleDto>(HttpMethod.Post, "api/kb/articles", request, cancellationToken);

    public Task<Result<KbArticleDto>> UpdateAsync(Guid id, UpdateKbArticleRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbArticleDto>(HttpMethod.Put, $"api/kb/articles/{id}", request, cancellationToken);

    public Task<Result<KbArticleDto>> PublishAsync(Guid id, uint version, CancellationToken cancellationToken) =>
        connection.SendAsync<KbArticleDto>(HttpMethod.Post, ApiUri.Build($"api/kb/articles/{id}/publish", ("version", version)), null, cancellationToken);

    public Task<Result<KbArticleDto>> ArchiveAsync(Guid id, uint version, CancellationToken cancellationToken) =>
        connection.SendAsync<KbArticleDto>(HttpMethod.Post, ApiUri.Build($"api/kb/articles/{id}/archive", ("version", version)), null, cancellationToken);

    public Task<Result<KbPreviewResponse>> PreviewAsync(KbPreviewRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbPreviewResponse>(HttpMethod.Post, "api/kb/preview", request, cancellationToken);

    public async Task<Result<KbImageUploadResponse>> UploadImageAsync(KbImageFile file, CancellationToken cancellationToken)
    {
        Stream? stream = null;
        try
        {
            stream = file.OpenRead();
            using var form = new MultipartFormDataContent();
            var part = new StreamContent(stream);
            part.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.ContentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");

            // The browser's file name is cleaned first: an empty or odd name would make the multipart body throw (the reply composer does the same).
            form.Add(part, KbLimits.ImageFieldName, AttachmentFileName.Clean(file.FileName));
            return await connection.SendContentAsync<KbImageUploadResponse>(HttpMethod.Post, "api/kb/images", form, cancellationToken);
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }
        }
    }

    public async Task<Result<IReadOnlyList<KbCategoryDto>>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<KbCategoryDto>>("api/kb/categories", cancellationToken));

    public Task<Result<KbCategoryDto>> CreateCategoryAsync(CreateKbCategoryRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbCategoryDto>(HttpMethod.Post, "api/kb/categories", request, cancellationToken);

    public Task<Result<KbCategoryDto>> UpdateCategoryAsync(Guid id, UpdateKbCategoryRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbCategoryDto>(HttpMethod.Put, $"api/kb/categories/{id}", request, cancellationToken);

    public Task<Result> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, $"api/kb/categories/{id}", null, cancellationToken);
}
