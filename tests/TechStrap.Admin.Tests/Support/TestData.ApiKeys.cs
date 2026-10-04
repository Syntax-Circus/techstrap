using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static ProductApiKeyDto ApiKey(
        string prefix = "tsk_ab12",
        string kind = ApiKeyKinds.Trusted,
        string? label = "Billing server",
        Guid? id = null,
        DateTimeOffset? created = null,
        DateTimeOffset? revoked = null,
        DateTimeOffset? lastUsed = null) => new(id ?? Guid.NewGuid(), OrbitlyId, kind, prefix, label, created ?? Now.AddDays(-3), revoked, lastUsed);
}
