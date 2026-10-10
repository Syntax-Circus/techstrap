using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Settings;

internal static class SettingsErrors
{
    public static ResultError PackUnknown() =>
        new("skin-pack-unknown", "Choose one of the available theme packs.", ResultErrorKind.Validation, "default-pack");

    public static ResultError Stale() =>
        new(PersistenceErrorCodes.ConcurrencyConflict, "The site settings changed since you opened them. Reload them and apply your change again.", ResultErrorKind.Conflict);
}
