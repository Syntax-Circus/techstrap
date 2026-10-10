using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.ApiKeys;
using TechStrap.Application.Products;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Controllers;

/// <summary>Products and branding. Agents read active products; admins manage them (D-022).</summary>
[ApiController]
[Route("api/products")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class ProductsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromServices] IListProductsRequestHandler listProducts, CancellationToken cancellationToken) =>
        (await listProducts.HandleAsync(cancellationToken)).ToActionResult(this, Ok);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromServices] IGetProductRequestHandler getProduct, CancellationToken cancellationToken) =>
        (await getProduct.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Create(CreateProductRequest request, [FromServices] ICreateProductRequestHandler createProduct, CancellationToken cancellationToken) =>
        (await createProduct.HandleAsync(request, cancellationToken)).ToActionResult(this, product => CreatedAtAction(nameof(Get), new { id = product.Id }, product));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request, [FromServices] IUpdateProductRequestHandler updateProduct, CancellationToken cancellationToken) =>
        (await updateProduct.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    [HttpGet("{id:guid}/api-keys")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> ListApiKeys(Guid id, [FromServices] IListProductApiKeysRequestHandler listApiKeys, CancellationToken cancellationToken) =>
        (await listApiKeys.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Creates a key. The response carries the plaintext key once; it cannot be shown again.</summary>
    [HttpPost("{id:guid}/api-keys")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> CreateApiKey(Guid id, CreateProductApiKeyRequest request, [FromServices] ICreateProductApiKeyRequestHandler createApiKey, CancellationToken cancellationToken)
    {
        // The body carries the plaintext key once, so no cache or proxy may keep it.
        Response.Headers.CacheControl = "no-store";
        return (await createApiKey.HandleAsync(id, request, cancellationToken)).ToActionResult(this, created => StatusCode(StatusCodes.Status201Created, created));
    }

    [HttpDelete("{id:guid}/api-keys/{keyId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> RevokeApiKey(Guid id, Guid keyId, [FromServices] IRevokeProductApiKeyRequestHandler revokeApiKey, CancellationToken cancellationToken) =>
        (await revokeApiKey.HandleAsync(id, keyId, cancellationToken)).ToActionResult(this, NoContent);

    /// <summary>One png, jpeg or webp up to 1 MiB, by its leading bytes (D-052). It supersedes the linked logo address; a second upload replaces the first.</summary>
    [HttpPost("{id:guid}/logo")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [ReadFormBeforeBinding]
    [RequestSizeLimit(ProductLogoRequestLimits.FormBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductLogoRequestLimits.FormBytes * 2)]
    public async Task<IActionResult> UploadLogo(Guid id, [FromForm] ProductLogoForm form, [FromServices] IUploadProductLogoRequestHandler uploadLogo, CancellationToken cancellationToken)
    {
        if (form.File is not { } file)
        {
            return (await uploadLogo.HandleAsync(id, null, cancellationToken)).ToActionResult(this, Ok);
        }

        await using var stream = file.OpenReadStream();
        return (await uploadLogo.HandleAsync(id, new IncomingProductLogo(file.Length, stream), cancellationToken)).ToActionResult(this, Ok);
    }

    /// <summary>Removes the uploaded logo; the linked logo address, if any, shows again. Idempotent.</summary>
    [HttpDelete("{id:guid}/logo")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> RemoveLogo(Guid id, [FromServices] IRemoveProductLogoRequestHandler removeLogo, CancellationToken cancellationToken) =>
        (await removeLogo.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);
}

/// <summary>Multipart form posted to upload one product logo: the file in the field named <c>file</c>.</summary>
public sealed class ProductLogoForm
{
    public IFormFile? File { get; set; }
}
