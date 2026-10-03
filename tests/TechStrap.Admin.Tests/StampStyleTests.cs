using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>BRAND.md sections 13, 17 and 18: straight single-border stamps in lists, tilted ones on the ticket view, and no motion when the user asks for none.</summary>
public sealed class StampStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Theory]
    [InlineData("new", "st-new")]
    [InlineData("open", "st-open")]
    [InlineData("pending", "st-pending")]
    [InlineData("solved", "st-solved")]
    [InlineData("closed", "st-closed")]
    [InlineData("spam", "st-spam")]
    public void Each_status_stamp_takes_its_status_token(string status, string token)
    {
        Css.Declarations($".ts-stamp--{status}")["--ts-stamp-color"].ShouldBe($"var(--{token})");
    }

    [Fact]
    public void Queue_stamps_are_straight_with_a_single_border()
    {
        var queue = Css.Declarations(".ts-stamp--queue");

        queue["border-width"].ShouldBe("1.5px");
        queue.ShouldNotContainKey("transform");
        queue.ShouldNotContainKey("box-shadow");
    }

    [Fact]
    public void Ticket_stamps_tilt_minus_two_degrees_with_an_inner_ring_and_spam_tilts_the_other_way()
    {
        var ticket = Css.Declarations(".ts-stamp--ticket");

        ticket["--ts-tilt"].ShouldBe("-2deg");
        ticket["transform"].ShouldBe("rotate(var(--ts-tilt))");
        ticket["box-shadow"].ShouldContain("inset");
        Css.Declarations(".ts-stamp--ticket.ts-stamp--spam")["--ts-tilt"].ShouldBe("2deg");
    }

    [Fact]
    public void Spam_has_a_double_three_pixel_border()
    {
        var spam = Css.Declarations(".ts-stamp--spam");

        spam["border-style"].ShouldBe("double");
        spam["border-width"].ShouldBe("3px");
    }

    [Fact]
    public void Spam_keeps_its_three_pixel_border_in_the_queue_too()
    {
        Css.Declarations(".ts-stamp--queue.ts-stamp--spam")["border-width"].ShouldBe("3px");
    }

    [Fact]
    public void Stamp_down_runs_for_0_35s_and_ends_at_the_tilt()
    {
        Css.Declarations(".ts-stamp--pop")["animation"].ShouldBe("ts-thud .35s ease-out");
        Regex.IsMatch(Css.Text, @"@keyframes ts-thud\{0%\{transform:rotate\(var\(--ts-tilt\)\) scale\(1\.6\);opacity:0\}70%\{[^}]*scale\(0?\.95\)[^}]*\}100%\{transform:rotate\(var\(--ts-tilt\)\) scale\(1\)\}\}").ShouldBeTrue();
    }

    [Fact]
    public void Reduced_motion_switches_off_every_animation_and_transition()
    {
        Regex.IsMatch(
            Css.Text,
            @"@media\s*\(prefers-reduced-motion:\s*reduce\)\{\*,\*::before,\*::after\{animation:\s*none\s*!important;transition:\s*none\s*!important\}\}").ShouldBeTrue();
    }
}
