namespace TechStrap.Admin.Clients;

/// <summary>
/// The error codes components branch on. Codes that come from the API are its problem <c>type</c> (or, for a validation failure, the entries of
/// <c>errorCodes</c>); the others are produced by the API connection when the API gave no code.
/// </summary>
public static class ApiErrorCodes
{
    // Produced by the Admin.
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not-found";
    public const string Conflict = "conflict";
    public const string ValidationFailed = "validation-failed";
    public const string ApiUnavailable = "api-unavailable";
    public const string ApiTimeout = "api-timeout";
    public const string UnexpectedResponse = "api-unexpected-response";
    public const string ApiError = "api-error";

    // Produced by the API.
    /// <summary>The API's exception middleware answers a 500 with this type; the Admin maps every 5xx by status, so it never reaches a component.</summary>
    public const string InternalError = "internal-error";
    public const string ConcurrencyConflict = "concurrency-conflict";
    public const string TicketClosed = "ticket-closed";
    public const string InvalidStatusTransition = "invalid-status-transition";
    public const string TicketNotFound = "ticket-not-found";
    public const string AgentNotFound = "agent-not-found";
    public const string TagNotFound = "tag-not-found";
    public const string ProductNotFound = "product-not-found";
    public const string RowVersionRequired = "row-version-required";
    public const string AgentAccessRequired = "agent-access-required";
    public const string AgentInactive = "agent-inactive";
    public const string AgentEmailRequired = "agent-email-required";
    public const string AgentIdentityInvalid = "agent-identity-invalid";
    public const string AgentNotProvisioned = "agent-not-provisioned";
    public const string AdminAccessRequired = "admin-access-required";
    public const string TagSlugTaken = "tag-slug-taken";
    public const string TagInUse = "tag-in-use";
    public const string LastActiveAdmin = "last-active-admin";
    public const string ProductKeyTaken = "product-key-taken";
    public const string ApiKeyKindInvalid = "api-key-kind-invalid";
    public const string ApiKeyNotFound = "api-key-not-found";
    public const string ApiKeyRevoked = "api-key-revoked";
    public const string OutboxNotFound = "outbox-not-found";
    public const string OutboxNotDeadLettered = "outbox-not-dead-lettered";
    public const string AdminEventSubjectTypeInvalid = "admin-event-subject-type-invalid";
    public const string PublicDisplayNameTooLong = "public-display-name-too-long";
    public const string PublicDisplayNameInvalid = "public-display-name-invalid";
    public const string LogoPathInvalid = "logo-path-invalid";
    public const string KbSlugTaken = "kb-slug-taken";
    public const string KbCategorySlugTaken = "kb-category-slug-taken";
    public const string KbCategoryReservedSlug = "kb-category-reserved-slug";
    public const string KbCategoryInUse = "kb-category-in-use";
    public const string KbCategoryScopeMismatch = "kb-category-scope-mismatch";
    public const string KbArticleNotFound = "kb-article-not-found";
    public const string KbCategoryNotFound = "kb-category-not-found";
    public const string KbPublishIncomplete = "kb-publish-incomplete";
    public const string KbImageTypeNotAllowed = "kb-image-type-not-allowed";
    public const string KbImageTooLarge = "kb-image-too-large";
    public const string KbArticleNotLinkable = "kb-article-not-linkable";
    public const string KbBodyTooComplex = "kb-body-too-complex";
    public const string ArticleAlreadyPublished = "article-already-published";
    public const string ArticleAlreadyArchived = "article-already-archived";

    /// <summary>Kestrel answers a request body far over the limit (413) with this problem type before any handler runs; a picture over 5 MB but under the request cap is <see cref="KbImageTooLarge"/>.</summary>
    public const string RequestTooLarge = "request-too-large";

    /// <summary>A reply that links an article id the API does not know (404). Linking an unpublished or other-product article is <see cref="KbArticleNotLinkable"/>.</summary>
    public const string ArticleNotFound = "article-not-found";

    /// <summary>A write that failed (any 5xx, transport error, timeout or unreadable answer) this way may still have been applied (the answer was lost, late or unreadable): say so and offer a reload, never a bare retry.</summary>
    public static bool IsUncertainWrite(string code) => code is ApiTimeout or ApiUnavailable or UnexpectedResponse or ApiError;
}
