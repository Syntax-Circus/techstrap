using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.DeadLetters;

/// <summary>Shared dead-letter outcomes. The messages are shown to agents: each gives the plain cause and the next step (BRAND.md section 3).</summary>
internal static class DeadLetterErrors
{
    public static ResultError NotFound() => new(PersistenceErrorCodes.OutboxNotFound, "That email does not exist.", ResultErrorKind.NotFound);
}
