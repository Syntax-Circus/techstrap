using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Tests.Components;

public sealed class FailedEmailCounterTests
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private readonly IDeadLettersClient _deadLetters = Substitute.For<IDeadLettersClient>();

    [Fact]
    public void The_count_is_unknown_until_it_is_read()
    {
        new FailedEmailCounter(_deadLetters).Count.ShouldBeNull();
    }

    [Fact]
    public async Task A_refresh_reads_the_total_and_announces_it()
    {
        _deadLetters.CountAsync(Ct).Returns(Result<int>.Success(4));
        var counter = new FailedEmailCounter(_deadLetters);
        var changes = 0;
        counter.Changed += () => changes++;

        await counter.RefreshAsync(Ct);

        counter.Count.ShouldBe(4);
        changes.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_last_known_number()
    {
        _deadLetters.CountAsync(Ct).Returns(Result<int>.Success(4), Result<int>.Failure(new ResultError(ApiErrorCodes.ApiUnavailable, "Down.", ResultErrorKind.Failure)));
        var counter = new FailedEmailCounter(_deadLetters);

        await counter.RefreshAsync(Ct);
        await counter.RefreshAsync(Ct);

        counter.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_failed_first_refresh_leaves_the_count_unknown()
    {
        _deadLetters.CountAsync(Ct).Returns(Result<int>.Failure(new ResultError(ApiErrorCodes.AdminAccessRequired, "No.", ResultErrorKind.Forbidden)));
        var counter = new FailedEmailCounter(_deadLetters);

        await counter.RefreshAsync(Ct);

        counter.Count.ShouldBeNull();
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void A_page_that_knows_the_total_sets_it_without_a_call(int total, int expected)
    {
        var counter = new FailedEmailCounter(_deadLetters);

        counter.Set(total);

        counter.Count.ShouldBe(expected);
        _deadLetters.ReceivedCalls().ShouldBeEmpty();
    }
}
