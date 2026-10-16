using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Skins;

namespace TechStrap.Admin.Features.Appearance;

public sealed partial class ProductSkinEditor
{
    [Parameter, EditorRequired] public ProductSkinDraft Draft { get; set; } = default!;
    [Parameter] public EventCallback OnChanged { get; set; }
    [Parameter] public string? Error { get; set; }
    private bool _removeOpen;
    private static readonly string[] Colours = ["background", "surface", "ink", "muted", "border", "brand", "chrome", "focus"];
    private static readonly string[] Fonts = ["headingFont", "bodyFont"];
    private static readonly (string Field, string[] Values)[] Presets =
    [
        ("radius", ["square", "soft", "round"]),
        ("borderWidth", ["1", "2", "3", "4"]),
        ("shadow", ["none", "soft", "hard"]),
        ("button", ["flat", "bevel", "outline"]),
        ("header", ["plain", "solid", "band"]),
    ];
    private string? Value(string field) => field switch
    {
        "pack" => Draft.Skin?.Pack,
        "background" => Draft.Skin?.Background,
        "surface" => Draft.Skin?.Surface,
        "ink" => Draft.Skin?.Ink,
        "muted" => Draft.Skin?.Muted,
        "border" => Draft.Skin?.Border,
        "brand" => Draft.Skin?.Brand,
        "chrome" => Draft.Skin?.Chrome,
        "focus" => Draft.Skin?.Focus,
        "headingFont" => Draft.Skin?.HeadingFont,
        "bodyFont" => Draft.Skin?.BodyFont,
        "radius" => Draft.Skin?.Radius,
        "borderWidth" => Draft.Skin?.BorderWidth?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "shadow" => Draft.Skin?.Shadow,
        "button" => Draft.Skin?.Button,
        "header" => Draft.Skin?.Header,
        _ => null,
    };

    private static string Label(string field) => field switch
    {
        "pack" => "Pack",
        "background" => "Background",
        "surface" => "Surface",
        "ink" => "Ink",
        "muted" => "Muted",
        "border" => "Border",
        "brand" => "Brand",
        "chrome" => "Chrome",
        "focus" => "Focus",
        "headingFont" => "Heading font",
        "bodyFont" => "Body font",
        "radius" => "Radius",
        "borderWidth" => "Border width (pixels)",
        "shadow" => "Shadow",
        "button" => "Button",
        "header" => "Header",
        _ => field,
    };

    private async Task Set(string field, ChangeEventArgs args) { Draft.Set(field, args.Value?.ToString()); await OnChanged.InvokeAsync(); }
    private async Task Reset(string field) { Draft.Set(field, null); await OnChanged.InvokeAsync(); }
    private async Task EditJson(ChangeEventArgs args) { Draft.EditJson(args.Value?.ToString() ?? string.Empty); await OnChanged.InvokeAsync(); }
    private async Task ApplyJson() { Draft.ApplyJson(); await OnChanged.InvokeAsync(); }
    private async Task DiscardJson() { Draft.DiscardJson(); await OnChanged.InvokeAsync(); }
    private async Task Remove() { _removeOpen = false; Draft.Remove(); await OnChanged.InvokeAsync(); }
    private string? Problem => Error ?? Draft.Problem();
}
