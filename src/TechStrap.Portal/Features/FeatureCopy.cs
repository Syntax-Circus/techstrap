using TechStrap.Contracts.Features;

namespace TechStrap.Portal.Features;

public static class FeatureCopy
{
    public static IReadOnlyList<string> Statuses => FeatureProgressStatuses.All;
    public static string Progress(string status) => status switch
    {
        "UnderConsideration" => "Under consideration",
        "InProgress" => "In progress",
        "NotPlanned" => "Not planned",
        "Planned" => "Planned",
        "Shipped" => "Shipped",
        _ => "Under consideration",
    };
}

