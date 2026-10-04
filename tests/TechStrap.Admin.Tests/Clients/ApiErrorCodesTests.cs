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
