using SyntaxCircus.Common;
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>The codes and field names the knowledge base screens branch on are the API's wire strings. A typo would make a screen treat a known refusal as a generic failure.</summary>
public sealed class KbErrorCodesTests
{
    [Theory]
    [InlineData(ApiErrorCodes.KbSlugTaken, "kb-slug-taken")]
    [InlineData(ApiErrorCodes.KbCategorySlugTaken, "kb-category-slug-taken")]
    [InlineData(ApiErrorCodes.KbCategoryReservedSlug, "kb-category-reserved-slug")]
    [InlineData(ApiErrorCodes.KbCategoryInUse, "kb-category-in-use")]
    [InlineData(ApiErrorCodes.KbCategoryScopeMismatch, "kb-category-scope-mismatch")]
    [InlineData(ApiErrorCodes.KbArticleNotFound, "kb-article-not-found")]
    [InlineData(ApiErrorCodes.KbCategoryNotFound, "kb-category-not-found")]
    [InlineData(ApiErrorCodes.KbPublishIncomplete, "kb-publish-incomplete")]
    [InlineData(ApiErrorCodes.KbImageTypeNotAllowed, "kb-image-type-not-allowed")]
    [InlineData(ApiErrorCodes.KbImageTooLarge, "kb-image-too-large")]
    [InlineData(ApiErrorCodes.KbArticleNotLinkable, "kb-article-not-linkable")]
    [InlineData(ApiErrorCodes.ArticleNotFound, "article-not-found")]
    [InlineData(ApiErrorCodes.RequestTooLarge, "request-too-large")]
    public void The_knowledge_base_codes_are_the_api_wire_codes(string constant, string wire) => constant.ShouldBe(wire);

    [Theory]
    [InlineData(ApiErrorCodes.KbSlugTaken)]
    [InlineData(ApiErrorCodes.KbCategorySlugTaken)]
    [InlineData(ApiErrorCodes.KbCategoryInUse)]
    [InlineData(ApiErrorCodes.KbPublishIncomplete)]
    [InlineData(ApiErrorCodes.KbArticleNotLinkable)]
    public void A_refusal_is_never_an_uncertain_write_and_is_classified_as_other(string code)
    {
        ApiErrorCodes.IsUncertainWrite(code).ShouldBeFalse();
        WriteOutcomes.Classify(new ResultError(code, "m", ResultErrorKind.Conflict)).ShouldBe(WriteOutcome.Other);
    }

    [Fact]
    public void The_knowledge_base_field_targets_are_the_kebab_case_names_the_api_sends()
    {
        string[] targets = [ApiFields.Title, ApiFields.Summary, ApiFields.Body, ApiFields.Category, ApiFields.Description, ApiFields.SortOrder, ApiFields.File];

        targets.ShouldBe(["title", "summary", "body", "category", "description", "sort-order", "file"]);
    }

    [Fact]
    public void A_category_error_on_a_request_is_named_by_the_request_property_and_a_publish_error_by_the_field()
    {
        ApiFields.CategoryId.ShouldBe("categoryId");
        ApiFields.Category.ShouldBe("category");
    }
}
