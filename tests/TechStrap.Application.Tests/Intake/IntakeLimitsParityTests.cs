using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tests.Intake;

public sealed class IntakeLimitsParityTests
{
    [Fact]
    public void The_per_file_limit_matches_the_domain_attachment_limit() =>
        IntakeLimits.MaxFileBytes.ShouldBe(DomainLimits.AttachmentMaxBytes);

    [Fact]
    public void The_message_limit_holds_the_maximum_number_of_maximum_files_or_fewer() =>
        IntakeLimits.MaxMessageBytes.ShouldBeLessThanOrEqualTo(IntakeLimits.MaxFiles * IntakeLimits.MaxFileBytes);

    [Fact]
    public void The_api_key_header_matches_the_authentication_package_default() =>
        HeaderNames.ApiKey.ShouldBe("X-Api-Key");

    [Fact]
    public void The_text_limits_the_portal_forms_use_match_the_domain()
    {
        // D-045 addendum (2026-10-06): Contracts repeats these so the contact form never offers a longer value than the API accepts.
        IntakeLimits.NameMaxLength.ShouldBe(DomainLimits.NameMaxLength);
        IntakeLimits.EmailMaxLength.ShouldBe(DomainLimits.EmailMaxLength);
        IntakeLimits.SubjectMaxLength.ShouldBe(DomainLimits.SubjectMaxLength);
        IntakeLimits.BodyMaxLength.ShouldBe(DomainLimits.MessageBodyMaxLength);
    }

    [Fact]
    public void The_form_body_limit_is_the_message_limit_plus_one_mebibyte_for_the_text_fields_and_multipart_framing()
    {
        IntakeLimits.FormBodyBytes.ShouldBe(IntakeLimits.MaxMessageBytes + (1024 * 1024));
        IntakeLimits.FormBodyBytes.ShouldBe(27_262_976);
    }

    [Fact]
    public void Allowed_extensions_are_lower_case_and_dotted() =>
        IntakeLimits.AllowedExtensions.ShouldAllBe(extension => extension.StartsWith('.') && extension == extension.ToLowerInvariant());
}
