using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>What the editor's choices and the portal link resolve against: the products an agent can see and every category.</summary>
internal sealed record KbEditorLookups(IReadOnlyList<ProductDto> Products, IReadOnlyList<KbCategoryDto> Categories)
{
    public static KbEditorLookups Empty { get; } = new([], []);

    /// <summary>
    /// The categories an article of <paramref name="productId"/> may use, in sort order: the shared ones, and (for a product article) the product's own. A shared article may use only a shared
    /// category, which is the API's rule (kb-category-scope-mismatch).
    /// </summary>
    public IReadOnlyList<KbCategoryDto> CategoriesFor(Guid? productId) =>
        [.. Categories.Where(c => c.ProductId is null || c.ProductId == productId).OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];

    public string ProductName(Guid? productId) => KbArticleRowViewModel.ProductNameOf(productId, Products);

    /// <summary>
    /// The product key an article's portal address is built with. A shared article is reachable under every product, so the first product by name stands in; null when there is no product to
    /// stand in (no link is shown then).
    /// </summary>
    public string? PortalProductKey(Guid? productId) => PortalProduct(productId)?.Key;

    /// <summary>The portal host of the product <see cref="PortalProductKey"/> resolves to, or null when it has none (the default host is used).</summary>
    public string? PortalProductHost(Guid? productId) => PortalProduct(productId)?.PortalHost;

    private ProductDto? PortalProduct(Guid? productId) => productId is { } id
        ? Products.FirstOrDefault(p => p.Id == id && p.IsActive)
        : Products.Where(p => p.IsActive).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

    public string? CategorySlug(Guid? categoryId) => categoryId is { } id ? Categories.FirstOrDefault(c => c.Id == id)?.Slug : null;
}

/// <summary>An article together with what the editor needs around it. The article is null when a new one is being written.</summary>
internal sealed record KbEditorData(KbArticleDto? Article, KbEditorLookups Lookups);

/// <summary>
/// Assembles the editor's data: the article, the products and the categories. It exists because the assembly is asynchronous and uses two clients (PHASE-08 allows exactly this presenter). The
/// article's own failure wins over a lookup failure, so a missing article is reported as missing; a lookup that 404s is turned into a plain failure so it can never read as "article not found".
/// </summary>
internal sealed class KbArticleEditorPresenter(IKbClient kb, IProductsClient products)
{
    /// <param name="articleId">The article to edit, or null for a new one.</param>
    public async Task<Result<KbEditorData>> LoadAsync(Guid? articleId, CancellationToken cancellationToken)
    {
        var articleTask = articleId is { } id ? kb.GetAsync(id, cancellationToken) : null;
        var productsTask = products.ListAsync(cancellationToken);
        var categoriesTask = kb.ListCategoriesAsync(cancellationToken);
        await Task.WhenAll(new Task?[] { articleTask, productsTask, categoriesTask }.OfType<Task>());

        if (articleTask is not null && articleTask.Result.IsFailure)
        {
            return Result<KbEditorData>.Failure(articleTask.Result.Errors[0]);
        }

        foreach (var lookup in new Result[] { productsTask.Result.ToResult(), categoriesTask.Result.ToResult() })
        {
            if (lookup.IsFailure)
            {
                var error = lookup.Errors[0];
                return Result<KbEditorData>.Failure(error.Kind == ResultErrorKind.NotFound ? new ResultError(error.Code, error.Message, ResultErrorKind.Failure) : error);
            }
        }

        return Result<KbEditorData>.Success(new KbEditorData(articleTask?.Result.Value, new KbEditorLookups(productsTask.Result.Value, categoriesTask.Result.Value)));
    }

    /// <summary>Reads the article again, for the screen's own refresh after a write; the lookups on screen are reused.</summary>
    public Task<Result<KbArticleDto>> ReloadAsync(Guid articleId, CancellationToken cancellationToken) => kb.GetAsync(articleId, cancellationToken);
}
