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
    public const int KeyPrefixMinLength = 4;
    public const int KeyPrefixMaxLength = 16;
    public const int HashMaxLength = 200;
    public const int MetadataMaxLength = 16_000;
    public const int ErrorMaxLength = 2_000;
    public const int KindMaxLength = 64;
}
