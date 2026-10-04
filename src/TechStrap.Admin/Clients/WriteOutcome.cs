using SyntaxCircus.Common;

namespace TechStrap.Admin.Clients;

/// <summary>How a failed ticket write is handled by a page. The pages branch on this and nothing else, so they all treat the same answer the same way.</summary>
public enum WriteOutcome
{
    /// <summary>Someone else changed the ticket: reload and apply the change again.</summary>
    Conflict,

    /// <summary>The ticket no longer exists (only <c>ticket-not-found</c>; another 404 names a missing agent, tag or product).</summary>
    Gone,

    /// <summary>The ticket is closed to this change.</summary>
    Closed,

    /// <summary>The write may have been applied before the answer was lost: never a bare "try again".</summary>
    Uncertain,

    /// <summary>Any other failure: the API's own message says why.</summary>
    Other,
}

public static class WriteOutcomes
{
    public static WriteOutcome Classify(ResultError error) => error.Code switch
    {
        ApiErrorCodes.ConcurrencyConflict => WriteOutcome.Conflict,
        ApiErrorCodes.TicketNotFound => WriteOutcome.Gone,
        ApiErrorCodes.TicketClosed => WriteOutcome.Closed,
        _ when ApiErrorCodes.IsUncertainWrite(error.Code) => WriteOutcome.Uncertain,
        _ => WriteOutcome.Other,
    };
}
