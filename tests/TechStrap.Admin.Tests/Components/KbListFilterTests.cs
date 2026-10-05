using TechStrap.Admin.Features.Kb;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The list filter is the one thing the URL, the select boxes and the API request share.</summary>
public sealed class KbListFilterTests
{
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    [Fact]
    public void The_address_leaves_out_page_one_and_empty_values_and_escapes_the_search()
    {
        KbListFilter.Empty.Uri().ShouldBe("/kb");
        new KbListFilter(ProductId, false, CategoryId, "Draft", "reset & more", 2).Uri()
            .ShouldBe($"/kb?product={ProductId}&category={CategoryId}&status=Draft&search=reset%20%26%20more&page=2");
        KbListFilter.Empty.WithProduct("shared").Uri().ShouldBe("/kb?product=shared");
    }

    [Theory]
    [InlineData("shared", true, false)]
    [InlineData("SHARED", true, false)]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000001", false, true)]
    [InlineData("not-a-guid", false, false)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void The_product_choice_is_shared_a_product_or_every_product_and_nothing_else(string? value, bool shared, bool product)
    {
        var filter = new KbListFilter(Guid.NewGuid(), false, null, null, null, 4).WithProduct(value);

        filter.SharedOnly.ShouldBe(shared);
        (filter.ProductId is not null).ShouldBe(product);
        filter.Page.ShouldBe(1);
    }

    [Fact]
    public void The_request_carries_the_filter_a_product_means_its_own_articles_and_the_text_is_trimmed()
    {
        new KbListFilter(ProductId, false, CategoryId, KbArticleStatuses.Published, "  reset  ", 3).ToRequest()
            .ShouldBe(new ListKbArticlesRequest(ProductId, false, false, KbArticleStatuses.Published, CategoryId, "reset", 3, 25));
        KbListFilter.Empty.WithProduct("shared").ToRequest().SharedOnly.ShouldBeTrue();
    }

    [Theory]
    [InlineData("draft", "Draft")]
    [InlineData(" PUBLISHED ", "Published")]
    [InlineData("archived", "Archived")]
    [InlineData("deleted", null)]
    [InlineData("", null)]
    public void A_status_is_one_of_the_three_in_its_canonical_spelling_or_dropped(string value, string? expected) =>
        KbDefaults.CanonicalStatus(value).ShouldBe(expected);

    [Fact]
    public void Only_a_set_filter_counts_as_a_filter()
    {
        KbListFilter.Empty.HasFilters.ShouldBeFalse();
        (KbListFilter.Empty with { Search = "  " }).HasFilters.ShouldBeFalse();
        (KbListFilter.Empty with { Page = 4 }).HasFilters.ShouldBeFalse();
        KbListFilter.Empty.WithProduct("shared").HasFilters.ShouldBeTrue();
        (KbListFilter.Empty with { Status = "Draft" }).HasFilters.ShouldBeTrue();
    }
}
