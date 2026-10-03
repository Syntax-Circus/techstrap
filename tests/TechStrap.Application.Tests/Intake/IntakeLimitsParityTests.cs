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
    public void Allowed_extensions_are_lower_case_and_dotted() =>
        IntakeLimits.AllowedExtensions.ShouldAllBe(extension => extension.StartsWith('.') && extension == extension.ToLowerInvariant());
}
