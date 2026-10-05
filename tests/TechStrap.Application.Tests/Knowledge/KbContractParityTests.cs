using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>Contracts is dependency-free, so it repeats a few Domain facts as constants. These tests keep the copies equal.</summary>
public sealed class KbContractParityTests
{
    [Fact]
    public void Status_names_match_the_domain_enum()
    {
        var names = typeof(KbArticleStatuses).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToArray();

        names.ShouldBe(Enum.GetNames<KbArticleStatus>(), ignoreOrder: true);
    }

    [Fact]
    public void The_reserved_category_slug_and_the_preview_limit_match_the_domain()
    {
        KbLimits.ReservedCategorySlug.ShouldBe(DomainLimits.KbReservedCategorySlug);
        KbLimits.MaxPreviewChars.ShouldBe(DomainLimits.KbBodyMaxLength);
        KbLimits.MaxSearchTextChars.ShouldBe(DomainLimits.SearchTextMaxLength);
    }
}
