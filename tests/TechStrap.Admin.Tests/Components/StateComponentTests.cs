using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class StateComponentTests : AdminComponentTest
{
    [Fact]
    public void Loading_draws_the_requested_skeleton_rows_and_announces_one_label()
    {
        var cut = Render<LoadingState>(p => p.Add(c => c.Rows, 3));

        cut.FindAll(".ts-skeleton").Count.ShouldBe(3);
        cut.FindAll(".ts-skeleton").ShouldAllBe(row => row.GetAttribute("aria-hidden") == "true");
        cut.Find("[role=status] .visually-hidden").TextContent.ShouldBe("Loading");
    }

    [Fact]
    public void An_error_is_an_alert_with_a_retry_that_fires_once_per_click()
    {
        var retries = 0;
        var cut = Render<ErrorState>(p => p
            .Add(c => c.Message, "Couldn't load tickets.")
            .Add(c => c.OnRetry, () => retries++));

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't load tickets.");
        cut.Find("button").TextContent.ShouldBe("Retry");

        cut.Find("button").Click();

        retries.ShouldBe(1);
    }

    [Fact]
    public void An_error_without_a_retry_callback_draws_no_button()
    {
        var cut = Render<ErrorState>(p => p.Add(c => c.Message, "Not allowed."));

        cut.FindAll("button").ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_state_is_plain_text_with_optional_content_and_no_brand_window()
    {
        var cut = Render<EmptyState>(p => p
            .Add(c => c.Heading, "No spam")
            .AddChildContent("<a href=\"/queue\">Back to the queue</a>"));

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No spam");
        cut.Find("a").TextContent.ShouldBe("Back to the queue");
        cut.FindAll(".ts-window").ShouldBeEmpty();
        cut.FindAll("img").ShouldBeEmpty();
    }
}
