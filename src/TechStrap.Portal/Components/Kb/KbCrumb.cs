namespace TechStrap.Portal.Components.Kb;

/// <summary>One step of a breadcrumb trail: a plain-text label and, for every step but the last, the address it links to (a path of this site).</summary>
public sealed record KbCrumb(string Label, string? Href = null);
