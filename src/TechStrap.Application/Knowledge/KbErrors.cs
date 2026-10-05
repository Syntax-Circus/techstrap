using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

/// <summary>The errors the KB handlers return. The codes are part of the API contract (D-044) and the Admin matches on them.</summary>
internal static class KbErrors
{
    public static ResultError PreviewTooLong() =>
        new("body-too-long", $"The text is longer than {KbLimits.MaxPreviewChars:N0} characters and cannot be previewed.", ResultErrorKind.Validation, "body");
}
