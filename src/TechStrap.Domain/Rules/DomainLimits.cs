namespace TechStrap.Domain.Rules;

/// <summary>Named field limits shared by Domain validation and (through the EF mapping) the column lengths.</summary>
public static class DomainLimits
{
    public const int NameMaxLength = 100;
    public const int LabelMaxLength = 100;
    public const int EmailMaxLength = 320;
    public const int SlugMaxLength = 40;
    public const int KbSlugMaxLength = 80;
    public const int UrlMaxLength = 500;

    /// <summary>The longest hostname a product can be served on (the DNS limit of 253 characters).</summary>
    public const int HostNameMaxLength = 253;
    public const int TagNameMaxLength = 50;
    public const int PublicDisplayNameMaxLength = 60;
    public const int SubjectMaxLength = 200;

    /// <summary>The longest OIDC subject (<c>sub</c> claim) stored for an agent.</summary>
    public const int OidcSubjectMaxLength = 200;

    public const int MessageBodyMaxLength = 100_000;
    public const int MessageIdMaxLength = 998;
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 127;
    public const int StorageKeyMaxLength = 500;
    public const long AttachmentMaxBytes = 10L * 1024 * 1024;
    public const int KbTitleMaxLength = 200;
    public const int KbSummaryMaxLength = 500;
    public const int KbBodyMaxLength = 200_000;
    public const int KbCategoryDescriptionMaxLength = 300;

    /// <summary>The category slug the portal uses for KB search (<c>/p/{key}/kb/search</c>), so no category may take it (D-044).</summary>
    public const string KbReservedCategorySlug = "search";
    public const int KeyPrefixMinLength = 4;
    public const int KeyPrefixMaxLength = 16;
    public const int HashMaxLength = 200;
    public const int MetadataMaxLength = 16_000;
    public const int ErrorMaxLength = 2_000;
    public const int KindMaxLength = 64;

    /// <summary>The longest email outbox payload (JSON text); the payload is template data, never a body or an attachment.</summary>
    public const int OutboxPayloadMaxLength = 16_000;

    /// <summary>The longest ticket number prefix a product can have (for example <c>ACME</c>).</summary>
    public const int NumberPrefixMaxLength = 10;

    /// <summary>The longest formatted ticket number (for example <c>ACME-142</c>): a prefix of up to 10 characters, the hyphen and up to 13 digits. <see cref="Tickets.TicketNumber"/> accepts any positive <see cref="long"/>, but a per-product counter cannot realistically pass 13 digits.</summary>
    public const int TicketNumberMaxLength = 24;

    /// <summary>The length of a hex colour such as <c>#1A2B3C</c>: the hash plus six digits.</summary>
    public const int ColourHexLength = 7;

    // A query limit, not a column length.
    /// <summary>The maximum search text; longer text is truncated.</summary>
    public const int SearchTextMaxLength = 200;
}
