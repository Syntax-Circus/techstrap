using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Skins;

namespace TechStrap.Application.Products;

/// <summary>The skin JSON to store; null clears it. A wrapper because a <c>Result</c> value cannot itself be null.</summary>
internal sealed record PreparedSkin(string? Json);

/// <summary>The save-time checks of a product skin (D-053): the grammar first, then the contrast of the result against the deployment's default pack.</summary>
internal static class ProductSkins
{
    /// <summary>
    /// <see cref="Prepare"/> with the default pack read from the settings; the settings are read only when a non-empty skin was sent, so a request that
    /// carries no skin costs no extra query.
    /// </summary>
    public static async Task<Result<PreparedSkin>> PrepareAsync(ISiteSettingsRepository siteSettings, ProductSkin? requested, string? accent, CancellationToken cancellationToken)
    {
        if (requested is null || requested.IsEmpty)
        {
            return Result<PreparedSkin>.Success(new PreparedSkin(null));
        }

        var settings = await siteSettings.GetAsync(cancellationToken);
        return Prepare(requested, accent, settings.DefaultPackKey);
    }

    /// <summary>
    /// The JSON to store for <paramref name="requested"/>: null for an empty skin, otherwise its compact form. The first problem is returned as
    /// a validation error whose target is the token or pair it names.
    /// </summary>
    /// <param name="requested">The skin the caller sent; null is treated as empty.</param>
    /// <param name="accent">The product's accent colour (the brand colour when the skin sets none).</param>
    /// <param name="defaultPack">The deployment's default pack key.</param>
    public static Result<PreparedSkin> Prepare(ProductSkin? requested, string? accent, string defaultPack)
    {
        if (requested is null || requested.IsEmpty)
        {
            return Result<PreparedSkin>.Success(new PreparedSkin(null));
        }

        var format = SkinRules.Validate(requested);
        if (format.Count > 0)
        {
            return Failed(format[0]);
        }

        var contrast = SkinResolver.Resolve(defaultPack, requested, accent).Problems.FirstOrDefault(p => p.Code == SkinRules.ContrastInvalidCode);
        if (contrast is not null)
        {
            return Failed(contrast);
        }

        var json = SkinSerializer.Serialize(requested);
        if (json is { Length: > SkinRules.MaxJsonLength })
        {
            return Result<PreparedSkin>.Failure(ProductErrors.SkinTooLong());
        }

        return Result<PreparedSkin>.Success(new PreparedSkin(json));
    }

    private static Result<PreparedSkin> Failed(SkinProblem problem) =>
        Result<PreparedSkin>.Failure(new ResultError(problem.Code, Message(problem), ResultErrorKind.Validation, problem.Target));

    private static string Message(SkinProblem problem) => problem.Code == SkinRules.ContrastInvalidCode
        ? $"The {problem.Target.Replace("/", " and ", StringComparison.Ordinal)} colours are too close to read together. Choose colours with more contrast."
        : $"The skin value for {problem.Target} is not valid. Choose one of the listed options or a colour such as #1F6FEB.";
}
