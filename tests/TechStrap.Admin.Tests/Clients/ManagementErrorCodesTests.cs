using SyntaxCircus.Common;
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>The codes the 07b pages branch on are the API's wire strings. A typo here would make a page treat a known conflict as a generic failure.</summary>
public sealed class ManagementErrorCodesTests
{
    [Theory]
    [InlineData(ApiErrorCodes.TagSlugTaken, "tag-slug-taken")]
    [InlineData(ApiErrorCodes.TagInUse, "tag-in-use")]
    [InlineData(ApiErrorCodes.LastActiveAdmin, "last-active-admin")]
    [InlineData(ApiErrorCodes.ProductKeyTaken, "product-key-taken")]
    [InlineData(ApiErrorCodes.ApiKeyKindInvalid, "api-key-kind-invalid")]
    [InlineData(ApiErrorCodes.ApiKeyNotFound, "api-key-not-found")]
    [InlineData(ApiErrorCodes.ApiKeyRevoked, "api-key-revoked")]
    [InlineData(ApiErrorCodes.OutboxNotFound, "outbox-not-found")]
    [InlineData(ApiErrorCodes.OutboxNotDeadLettered, "outbox-not-dead-lettered")]
    [InlineData(ApiErrorCodes.AdminEventSubjectTypeInvalid, "admin-event-subject-type-invalid")]
    [InlineData(ApiErrorCodes.PublicDisplayNameTooLong, "public-display-name-too-long")]
    [InlineData(ApiErrorCodes.PublicDisplayNameInvalid, "public-display-name-invalid")]
    [InlineData(ApiErrorCodes.LogoPathInvalid, "logo-path-invalid")]
    public void The_management_codes_are_the_api_wire_codes(string constant, string wire) => constant.ShouldBe(wire);

    [Theory]
    [InlineData(ApiErrorCodes.TagSlugTaken)]
    [InlineData(ApiErrorCodes.TagInUse)]
    [InlineData(ApiErrorCodes.LastActiveAdmin)]
    [InlineData(ApiErrorCodes.ProductKeyTaken)]
    [InlineData(ApiErrorCodes.OutboxNotFound)]
    [InlineData(ApiErrorCodes.OutboxNotDeadLettered)]
    public void A_refusal_is_never_an_uncertain_write_and_is_classified_as_other(string code)
    {
        ApiErrorCodes.IsUncertainWrite(code).ShouldBeFalse();
        WriteOutcomes.Classify(new ResultError(code, "m", ResultErrorKind.Conflict)).ShouldBe(WriteOutcome.Other);
    }

    [Fact]
    public void The_field_targets_are_the_kebab_case_names_the_api_sends()
    {
        string[] targets =
        [
            ApiFields.Key, ApiFields.Name, ApiFields.NumberPrefix, ApiFields.DisplayName, ApiFields.LogoPath, ApiFields.AccentColour, ApiFields.FromAddress, ApiFields.ReplyTo,
            ApiFields.Kind, ApiFields.Label, ApiFields.Slug, ApiFields.Colour, ApiFields.PublicDisplayName,
        ];

        targets.ShouldBe(
            ["key", "name", "number-prefix", "display-name", "logo-path", "accent-colour", "from-address", "reply-to", "kind", "label", "slug", "colour", "public-display-name"]);
    }
}
