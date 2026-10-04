using TechStrap.Admin.Features.Settings;
using TechStrap.Contracts.DeadLetters;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static DeadLetterDto DeadLetter(
        string kind = EmailKinds.AgentReply,
        string recipient = "a***@example.com",
        Guid? ticketId = null,
        int attempts = 5,
        string? lastError = "smtp-transient",
        Guid? id = null,
        DateTimeOffset? created = null) => new(id ?? Guid.NewGuid(), kind, recipient, ticketId, OrbitlyId, attempts, lastError, created ?? Now.AddHours(-3));
}
