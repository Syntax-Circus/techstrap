using SyntaxCircus.Common;

namespace TechStrap.Application.Agents;

/// <summary>Agent access outcomes. The messages are shown to agents: each gives the plain cause and the next step (BRAND.md section 3).</summary>
internal static class AgentErrors
{
    public static class Codes
    {
        public const string AccessRequired = "agent-access-required";
        public const string EmailRequired = "agent-email-required";
        public const string IdentityInvalid = "agent-identity-invalid";
        public const string NotProvisioned = "agent-not-provisioned";
        public const string Inactive = "agent-inactive";
        public const string NotFound = "agent-not-found";
        public const string LastActiveAdmin = "last-active-admin";
    }

    public static ResultError AccessRequired() =>
        new(Codes.AccessRequired, "Your account is not in the TechStrap agent or admin group. Ask your identity provider administrator to add you.", ResultErrorKind.Forbidden);

    public static ResultError EmailRequired() =>
        new(Codes.EmailRequired, "Your sign-in did not include an email address. Ask your identity provider administrator to release the email claim to TechStrap.", ResultErrorKind.Forbidden);

    public static ResultError IdentityInvalid(string detail) =>
        new(Codes.IdentityInvalid, $"Your sign-in details could not be used: {detail} Ask your identity provider administrator to check your profile.", ResultErrorKind.Forbidden);

    public static ResultError NotProvisioned() =>
        new(Codes.NotProvisioned, "Open TechStrap once to finish signing in, then try again.", ResultErrorKind.Forbidden);

    public static ResultError Inactive() =>
        new(Codes.Inactive, "Your TechStrap access is turned off. Ask an admin to reactivate it.", ResultErrorKind.Forbidden);

    public static ResultError NotFound() =>
        new(Codes.NotFound, "That agent does not exist.", ResultErrorKind.NotFound);

    public static ResultError LastActiveAdmin() =>
        new(Codes.LastActiveAdmin, "TechStrap needs at least one active admin. Make sure another admin is active before turning this one off.", ResultErrorKind.Conflict);
}
