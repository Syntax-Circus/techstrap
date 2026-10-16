namespace TechStrap.Admin.Features.Features;

/// <summary>Human-readable feature progress, shared by the board editor and ticket picker.</summary>
public static class FeatureCopy
{
    public static string Progress(string value) => value switch
    {
        "UnderConsideration" => "Under consideration",
        "InProgress" => "In progress",
        "NotPlanned" => "Not planned",
        _ => value,
    };
}
