using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
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
    public async Task<IActionResult> CreateApiKey(Guid id, CreateProductApiKeyRequest request, [FromServices] ICreateProductApiKeyRequestHandler createApiKey, CancellationToken cancellationToken) =>
        (await createApiKey.HandleAsync(id, request, cancellationToken)).ToActionResult(this, created => StatusCode(StatusCodes.Status201Created, created));

    [HttpDelete("{id:guid}/api-keys/{keyId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> RevokeApiKey(Guid id, Guid keyId, [FromServices] IRevokeProductApiKeyRequestHandler revokeApiKey, CancellationToken cancellationToken) =>
        (await revokeApiKey.HandleAsync(id, keyId, cancellationToken)).ToActionResult(this, NoContent);
}
