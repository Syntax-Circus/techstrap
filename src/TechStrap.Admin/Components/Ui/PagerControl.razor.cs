using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>Previous/next paging with a "26-50 of 163" summary. It only reports the requested page; the owner of the list decides what to do with it.</summary>
public partial class PagerControl
{
    /// <summary>The current page, 1-based.</summary>
    [Parameter, EditorRequired]
    public int Page { get; set; } = 1;

    [Parameter, EditorRequired]
    public int PageSize { get; set; } = 1;

    [Parameter, EditorRequired]
    public int TotalCount { get; set; }

    [Parameter]
    public EventCallback<int> OnPageChanged { get; set; }

    private int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));

    /// <summary>The page shown: <see cref="Page"/> kept inside the range, so a stale page number (the list shrank) never prints "201-60 of 60".</summary>
    private int CurrentPage => Math.Clamp(Page, 1, PageCount);

    private int First => ((CurrentPage - 1) * PageSize) + 1;

    private int Last => Math.Min(TotalCount, CurrentPage * PageSize);

    private Task PreviousAsync() => CurrentPage > 1 ? OnPageChanged.InvokeAsync(CurrentPage - 1) : Task.CompletedTask;

    private Task NextAsync() => CurrentPage < PageCount ? OnPageChanged.InvokeAsync(CurrentPage + 1) : Task.CompletedTask;
}
