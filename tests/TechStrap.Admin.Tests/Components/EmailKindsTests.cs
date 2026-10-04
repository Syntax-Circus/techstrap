using TechStrap.Admin.Features.Settings;

namespace TechStrap.Admin.Tests.Components;

public sealed class EmailKindsTests
{
    [Fact]
    public void Known_kinds_have_labels_and_blank_is_just_email()
    {
        EmailKinds.Label(EmailKinds.AgentReply).ShouldBe("Agent reply");
        EmailKinds.Label(null).ShouldBe("Email");
        EmailKinds.Label(string.Empty).ShouldBe("Email");
    }

    [Fact]
    public void An_unknown_kind_is_shown_shortened_with_control_and_format_characters_replaced()
    {
        EmailKinds.Label("odd\u202Ekind\n").ShouldBe("odd kind");
        var label = EmailKinds.Label(new string('k', 300));
        label.Length.ShouldBe(60);
        label.ShouldEndWith("\u2026");
    }
}
