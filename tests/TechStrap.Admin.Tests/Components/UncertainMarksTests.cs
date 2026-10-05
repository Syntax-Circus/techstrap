using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// A write with an unknown outcome is held until a read that started after it has finished. A read that was already under way may have been answered before the write landed, so it must not release the hold.
/// </summary>
public sealed class UncertainMarksTests
{
    private static readonly Guid Row = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("11111111-0000-0000-0000-000000000002");

    [Fact]
    public void A_row_is_held_from_the_moment_it_is_marked()
    {
        var marks = new UncertainMarks();
        marks.Contains(Row).ShouldBeFalse();

        marks.Add(Row, latestLoadId: 3);

        marks.Contains(Row).ShouldBeTrue();
        marks.Contains(Other).ShouldBeFalse();
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(3, 2, true)]
    [InlineData(3, 4, false)]
    [InlineData(3, 9, false)]
    public void Only_a_read_that_started_after_the_mark_releases_it(int markedDuringLoad, int finishingLoad, bool stillHeld)
    {
        var marks = new UncertainMarks();
        marks.Add(Row, markedDuringLoad);

        marks.ReleaseForLoad(finishingLoad);

        marks.Contains(Row).ShouldBe(stillHeld);
    }

    [Fact]
    public void A_finishing_read_releases_the_older_marks_and_keeps_the_newer_ones()
    {
        var marks = new UncertainMarks();
        marks.Add(Row, 1);
        marks.Add(Other, 4);

        marks.ReleaseForLoad(3);

        marks.Contains(Row).ShouldBeFalse();
        marks.Contains(Other).ShouldBeTrue();
    }

    [Fact]
    public void Marking_a_row_again_takes_the_newer_load_id()
    {
        var marks = new UncertainMarks();
        marks.Add(Row, 1);
        marks.Add(Row, 5);

        marks.ReleaseForLoad(4);

        marks.Contains(Row).ShouldBeTrue();
    }

    [Fact]
    public void Clear_forgets_every_mark()
    {
        var marks = new UncertainMarks();
        marks.Add(Row, 1);
        marks.Add(Other, 2);

        marks.Clear();

        marks.Contains(Row).ShouldBeFalse();
        marks.Contains(Other).ShouldBeFalse();
    }
}
