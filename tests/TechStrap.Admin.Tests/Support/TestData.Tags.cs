using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static TagSummaryDto TagSummary(string name = "bug", int count = 0, string? slug = null, string colour = "#DC2626", Guid? id = null) =>
        new(id ?? Guid.NewGuid(), slug ?? name.ToLowerInvariant().Replace(' ', '-'), name, colour, count);

    public static AdminEventDto AdminEvent(
        string type = AdminEventTypes.TagCreated,
        string payload = "{\"slug\":\"bug\"}",
        string subjectType = AdminSubjectTypes.Tag,
        string? actor = "Ada Admin",
        DateTimeOffset? at = null,
        Guid? id = null) => new(id ?? Guid.NewGuid(), type, AdaAgentId, actor, subjectType, Guid.NewGuid(), payload, at ?? Now.AddMinutes(-5));
}
