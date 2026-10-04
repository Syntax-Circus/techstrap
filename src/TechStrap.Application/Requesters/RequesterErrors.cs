using SyntaxCircus.Common;

namespace TechStrap.Application.Requesters;

/// <summary>Shared requester outcomes. The messages are shown to agents: each gives the plain cause and the next step (BRAND.md section 3).</summary>
internal static class RequesterErrors
{
    public static ResultError NotFound() => new("requester-not-found", "That requester does not exist.", ResultErrorKind.NotFound);
}
