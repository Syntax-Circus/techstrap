using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

/// <summary>
/// The errors the KB handlers return. The codes are part of the API contract (D-044) and the Admin matches on them. A thing named in the
/// route that does not exist is a NotFound; a thing named in the body that does not exist is a Validation error on that field.
/// </summary>
internal static class KbErrors
{
    public static ResultError PreviewTooLong() =>
        new("body-too-long", $"The text is longer than {KbLimits.MaxPreviewChars:N0} characters and cannot be previewed.", ResultErrorKind.Validation, "body");

    public static ResultError BodyTooComplex() =>
        new("kb-body-too-complex", $"The text has too many elements (more than {KbLimits.MaxRenderedElements:N0} paragraphs, list items, table cells and similar). Split it into several articles or shorten the table or list.", ResultErrorKind.Validation, "body");

    public static ResultError ArticleNotFound() => new("kb-article-not-found", "That article does not exist.", ResultErrorKind.NotFound);

    public static ResultError CategoryNotFound() => new("kb-category-not-found", "That category does not exist.", ResultErrorKind.NotFound);

    public static ResultError CategoryNotFoundInBody() =>
        new("kb-category-not-found", "That category does not exist.", ResultErrorKind.Validation, "categoryId");

    public static ResultError ProductNotFoundInBody() =>
        new("product-not-found", "That product does not exist.", ResultErrorKind.Validation, "productId");

    public static ResultError StatusInvalid() =>
        new("status-invalid", "The status must be Draft, Published or Archived.", ResultErrorKind.Validation, "status");

    public static ResultError SlugTaken() =>
        new("kb-slug-taken", "Another article already uses this slug, here or in the shared space. Choose a different one.", ResultErrorKind.Conflict);

    public static ResultError CategorySlugTaken() =>
        new("kb-category-slug-taken", "Another category already uses this slug, here or in the shared space. Choose a different one.", ResultErrorKind.Conflict);

    public static ResultError CategoryScopeMismatch() =>
        new(
            "kb-category-scope-mismatch",
            "A shared article can only use a shared category, and a product article only a shared category or one of its own product.",
            ResultErrorKind.Validation,
            "categoryId");

    public static ResultError Stale(string what) =>
        new(PersistenceErrorCodes.ConcurrencyConflict, $"This {what} changed since you opened it. Reload it and apply your change again.", ResultErrorKind.Conflict);
}
