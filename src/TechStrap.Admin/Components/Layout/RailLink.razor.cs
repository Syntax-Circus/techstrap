using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// A link in the rail. It is current when the page is its address or anywhere below it (<c>/settings/products/abc</c> keeps Products current), and says so with
/// <c>aria-current="page"</c>, which is what the screen reader announces and what the rail's style reads. The framework's <c>NavLink</c> only adds a class, which a screen
/// reader never hears. The queue's view tabs and rows already use the same attribute.
/// </summary>
public sealed partial class RailLink : IDisposable
{
    [Parameter, EditorRequired]
    public string Href { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private bool IsCurrent => IsCurrentAddress(Navigation.ToBaseRelativePath(Navigation.Uri), Href);

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);

    /// <summary>
    /// True when <paramref name="relativePath"/> (the page's address relative to the base, with any query or fragment) is <paramref name="href"/> or below it, compared on whole path
    /// segments and ignoring case: <c>queue/mine</c> is under <c>/queue</c>, <c>queued</c> is not.
    /// </summary>
    public static bool IsCurrentAddress(string relativePath, string href)
    {
        var end = relativePath.IndexOfAny(['?', '#']);
        var path = (end < 0 ? relativePath : relativePath[..end]).Trim('/');
        var target = href.Trim('/');
        return path.Equals(target, StringComparison.OrdinalIgnoreCase)
            || (target.Length > 0 && path.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;
}
