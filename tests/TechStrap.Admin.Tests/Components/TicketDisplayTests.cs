using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TicketDisplayTests : AdminComponentTest
{
    public static TheoryData<string, StampStatus> Statuses() => new()
    {
        { TicketStatuses.New, StampStatus.New },
        { TicketStatuses.Open, StampStatus.Open },
        { TicketStatuses.Pending, StampStatus.Pending },
        { TicketStatuses.Solved, StampStatus.Solved },
        { TicketStatuses.Closed, StampStatus.Closed },
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Every_status_constant_maps_to_its_stamp(string status, StampStatus expected) =>
        TicketDisplay.Stamp(status, isSpam: false).ShouldBe(expected);

    [Theory]
    [MemberData(nameof(Statuses))]
    public void The_spam_flag_wins_over_any_status(string status, StampStatus _) =>
        TicketDisplay.Stamp(status, isSpam: true).ShouldBe(StampStatus.Spam);

    [Fact]
    public void An_unknown_status_falls_back_to_the_neutral_open_stamp_and_logs_a_warning()
    {
        var log = new CapturingLogger();

        TicketDisplay.Stamp("Archived", isSpam: false, log).ShouldBe(StampStatus.Open);

        log.Warnings.ShouldHaveSingleItem().ShouldContain("Archived");
    }

    [Fact]
    public void A_known_status_and_a_spam_ticket_log_nothing()
    {
        var log = new CapturingLogger();

        TicketDisplay.Stamp(TicketStatuses.Pending, isSpam: false, log);
        TicketDisplay.Stamp("Archived", isSpam: true, log).ShouldBe(StampStatus.Spam);

        log.Warnings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(TicketPriorities.Urgent, PriorityLevel.Urgent)]
    [InlineData(TicketPriorities.High, PriorityLevel.High)]
    [InlineData(TicketPriorities.Normal, PriorityLevel.Normal)]
    [InlineData(TicketPriorities.Low, PriorityLevel.Low)]
    public void Every_priority_constant_maps_to_its_level(string priority, PriorityLevel expected) =>
        TicketDisplay.Priority(priority).ShouldBe(expected);

    [Fact]
    public void An_unknown_priority_falls_back_to_normal_and_logs_a_warning()
    {
        var log = new CapturingLogger();

        TicketDisplay.Priority("Critical", log).ShouldBe(PriorityLevel.Normal);

        log.Warnings.ShouldHaveSingleItem().ShouldContain("Critical");
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(2 * 86400, "2 d ago")]
    [InlineData(8 * 86400, "2026-09-26")]
    public void Relative_time_reads_like_a_person_would_say_it(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        TicketDisplay.Relative(now.AddSeconds(-secondsAgo), now).ShouldBe(expected);
    }

    [Fact]
    public void A_time_in_the_future_reads_as_just_now()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        TicketDisplay.Relative(now.AddMinutes(3), now).ShouldBe("just now");
    }

    [Theory]
    [InlineData("Sam Ortiz", "SO")]
    [InlineData("sam", "S")]
    [InlineData("  ada   lovelace byron ", "AL")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Initials_are_at_most_two_uppercase_letters(string? name, string expected) =>
        TicketDisplay.Initials(name).ShouldBe(expected);

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10L * 1024 * 1024, "10 MB")]
    public void File_sizes_use_one_decimal_and_the_invariant_culture(long bytes, string expected) =>
        TicketDisplay.FileSize(bytes).ShouldBe(expected);

    [Fact]
    public void The_relative_time_component_keeps_the_machine_time_and_the_utc_tooltip()
    {
        var when = Time.GetUtcNow().AddMinutes(-5);

        var cut = Render<RelativeTime>(p => p.Add(c => c.When, when));

        var element = cut.Find("time");
        element.TextContent.ShouldBe("5 min ago");
        element.GetAttribute("datetime").ShouldBe("2026-10-04T11:55:00.0000000Z");
        element.GetAttribute("title").ShouldBe("2026-10-04 11:55 UTC");
    }
}
