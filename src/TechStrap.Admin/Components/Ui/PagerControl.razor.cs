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

    private int First => ((Page - 1) * PageSize) + 1;

    private int Last => Math.Min(TotalCount, Page * PageSize);

    private Task PreviousAsync() => Page > 1 ? OnPageChanged.InvokeAsync(Page - 1) : Task.CompletedTask;

    private Task NextAsync() => Page < PageCount ? OnPageChanged.InvokeAsync(Page + 1) : Task.CompletedTask;
}
