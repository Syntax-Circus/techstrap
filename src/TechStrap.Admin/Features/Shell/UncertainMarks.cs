namespace TechStrap.Admin.Features.Shell;

/// <summary>
/// The writes whose outcome is unknown, held per row until the list has been read again (the rule of every admin page: a lost answer may still have been applied, so asking again sends nothing).
/// A mark is released by a read that started after the mark was made, and only by one. A read that was already under way when the write was sent may have been answered before the write
/// landed, so its rows can still show the old state; if it released the mark, the agent could send the write a second time. Each mark therefore carries the id of the latest read that had started
/// when it was made (<c>_loadId</c> on the page), and a finishing read with a higher id releases it. Not thread-safe, because a page touches it only on the renderer's thread.
/// </summary>
public sealed class UncertainMarks
{
    private readonly Dictionary<Guid, int> _marks = [];

    /// <summary>True while a write on this row has an unknown outcome and no later read has finished.</summary>
    public bool Contains(Guid id) => _marks.ContainsKey(id);

    /// <summary>Holds the row. <paramref name="latestLoadId"/> is the id of the latest read that has started so far (the page's own counter).</summary>
    public void Add(Guid id, int latestLoadId) => _marks[id] = latestLoadId;

    /// <summary>Called when the read with id <paramref name="loadId"/> has been applied: releases every mark made before that read started.</summary>
    public void ReleaseForLoad(int loadId)
    {
        foreach (var id in _marks.Where(mark => mark.Value < loadId).Select(mark => mark.Key).ToList())
        {
            _marks.Remove(id);
        }
    }

    /// <summary>Forgets every mark: the page now shows something else (another product), so the rows the marks were about are gone.</summary>
    public void Clear() => _marks.Clear();
}
