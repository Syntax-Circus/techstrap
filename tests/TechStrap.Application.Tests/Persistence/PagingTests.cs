using TechStrap.Application.Persistence;

namespace TechStrap.Application.Tests.Persistence;

public sealed class PagingTests
{
    [Theory]
    [InlineData(0, Paging.DefaultPageSize)]
    [InlineData(-5, Paging.DefaultPageSize)]
    [InlineData(int.MinValue, Paging.DefaultPageSize)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(Paging.MaxPageSize, Paging.MaxPageSize)]
    [InlineData(Paging.MaxPageSize + 1, Paging.MaxPageSize)]
    [InlineData(int.MaxValue, Paging.MaxPageSize)]
    public void Page_size_is_defaulted_below_one_and_capped_at_the_maximum(int requested, int expected)
    {
        Paging.NormalizePageSize(requested).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Page_is_at_least_one(int requested, int expected)
    {
        Paging.NormalizePage(requested).ShouldBe(expected);
    }

    [Fact]
    public void A_huge_page_is_clamped_so_the_offset_cannot_overflow()
    {
        var page = Paging.NormalizePage(int.MaxValue);
        var size = Paging.NormalizePageSize(int.MaxValue);

        page.ShouldBeLessThanOrEqualTo(Paging.MaxPage);
        checked((page - 1) * size).ShouldBeGreaterThanOrEqualTo(0);
        Paging.Offset(int.MaxValue, int.MaxValue).ShouldBe(checked((Paging.MaxPage - 1) * Paging.MaxPageSize));
    }

    [Theory]
    [InlineData(1, 25, 0)]
    [InlineData(3, 25, 50)]
    [InlineData(0, 0, 0)]
    [InlineData(2, 500, 100)]
    public void Offset_normalizes_both_values(int page, int pageSize, int expected)
    {
        Paging.Offset(page, pageSize).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, Paging.DefaultBatchSize)]
    [InlineData(-1, Paging.DefaultBatchSize)]
    [InlineData(10, 10)]
    [InlineData(Paging.MaxBatchSize, Paging.MaxBatchSize)]
    [InlineData(Paging.MaxBatchSize + 1, Paging.MaxBatchSize)]
    [InlineData(int.MaxValue, Paging.MaxBatchSize)]
    public void Batch_size_is_defaulted_below_one_and_capped(int requested, int expected)
    {
        Paging.NormalizeBatchSize(requested).ShouldBe(expected);
    }
}
