using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Tests.Clients;

public sealed class ApiErrorCodesTests
{
    [Theory]
    [InlineData(ApiErrorCodes.ApiTimeout, true)]
    [InlineData(ApiErrorCodes.ApiUnavailable, true)]
    [InlineData(ApiErrorCodes.UnexpectedResponse, true)]
    [InlineData(ApiErrorCodes.ApiError, true)]
    [InlineData(ApiErrorCodes.Forbidden, false)]
    [InlineData(ApiErrorCodes.ConcurrencyConflict, false)]
    [InlineData(ApiErrorCodes.TicketNotFound, false)]
    [InlineData(ApiErrorCodes.ValidationFailed, false)]
    public void Only_the_codes_that_leave_the_outcome_unknown_are_uncertain_writes(string code, bool expected) =>
        ApiErrorCodes.IsUncertainWrite(code).ShouldBe(expected);
}

public sealed class WriteOutcomeTests
{
    [Theory]
    [InlineData(ApiErrorCodes.ConcurrencyConflict, WriteOutcome.Conflict)]
    [InlineData(ApiErrorCodes.TicketNotFound, WriteOutcome.Gone)]
    [InlineData(ApiErrorCodes.TagNotFound, WriteOutcome.Other)]
    [InlineData(ApiErrorCodes.TicketClosed, WriteOutcome.Closed)]
    [InlineData(ApiErrorCodes.ApiTimeout, WriteOutcome.Uncertain)]
    [InlineData(ApiErrorCodes.ApiError, WriteOutcome.Uncertain)]
    [InlineData(ApiErrorCodes.InvalidStatusTransition, WriteOutcome.Other)]
    public void Every_failed_write_lands_in_exactly_one_outcome(string code, WriteOutcome expected) =>
        WriteOutcomes.Classify(new SyntaxCircus.Common.ResultError(code, "m", SyntaxCircus.Common.ResultErrorKind.Failure)).ShouldBe(expected);
}
