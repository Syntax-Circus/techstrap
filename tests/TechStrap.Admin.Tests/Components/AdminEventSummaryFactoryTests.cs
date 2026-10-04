using System.Reflection;
using TechStrap.Admin.Features.Settings.Audit;
using TechStrap.Contracts.AdminEvents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// PHASE-07 T17: the factory theory covers every admin event type constant. It reads ids, slugs, prefixes, kinds and counts only, never returns a payload, and never throws for an unknown type, a payload that
/// is not JSON, or a field of the wrong kind.
/// </summary>
public sealed class AdminEventSummaryFactoryTests
{
    private static readonly Dictionary<string, (string Payload, string Expected)> Samples = new()
    {
        [AdminEventTypes.ProductCreated] = ("{\"productKey\":\"orbitly\",\"numberPrefix\":\"ORB\"}", "Created product orbitly (ticket prefix ORB)"),
        [AdminEventTypes.ProductUpdated] = ("{\"changed\":[\"name\",\"branding\",\"isActive\"]}", "Updated a product: name, branding, active status"),
        [AdminEventTypes.ApiKeyCreated] = ("{\"productId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"Trusted\",\"keyPrefix\":\"tsk_ab12\"}", "Created a Trusted API key (tsk_ab12)"),
        [AdminEventTypes.ApiKeyRevoked] = ("{\"productId\":\"11111111-1111-1111-1111-111111111111\",\"keyPrefix\":\"tsk_ab12\"}", "Revoked API key tsk_ab12"),
        [AdminEventTypes.AgentUpdated] = ("{\"isActive\":false}", "Deactivated an agent"),
        [AdminEventTypes.TagCreated] = ("{\"slug\":\"bug\"}", "Created tag bug"),
        [AdminEventTypes.TagUpdated] = ("{\"slug\":\"bug\",\"changed\":[\"name\",\"colour\"]}", "Updated tag bug: name, colour"),
        [AdminEventTypes.TagDeleted] = ("{\"slug\":\"bug\",\"detachedTicketCount\":12}", "Deleted tag bug, removed from 12 tickets"),
        [AdminEventTypes.RequesterErased] = ("{\"tickets\":3,\"messages\":12,\"attachments\":1,\"links\":2,\"outboxRows\":4}", "Erased a requester: 3 tickets, 12 messages, 1 attachment, 2 access links, 4 queued emails"),
        [AdminEventTypes.TicketDeleted] = ("{\"number\":\"ORB-42\",\"messageCount\":1,\"attachmentCount\":0}", "Deleted ticket ORB-42 (1 message, 0 attachments)"),
        [AdminEventTypes.DeadLetterRetried] = ("{\"kind\":\"agent-reply\",\"attempts\":5}", "Retried a failed agent reply email after 5 attempts"),
        [AdminEventTypes.DeadLetterDiscarded] = ("{\"kind\":\"new-ticket-alert\",\"attempts\":1}", "Discarded a failed new ticket alert email after 1 attempt"),
    };

    private static IEnumerable<string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!);

    public static TheoryData<string> EveryEventType()
    {
        var data = new TheoryData<string>();
        foreach (var type in Constants(typeof(AdminEventTypes)))
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void There_is_a_sample_for_every_event_type_constant_so_a_new_type_fails_here_until_it_has_a_sentence()
    {
        Constants(typeof(AdminEventTypes)).OrderBy(t => t).ShouldBe(Samples.Keys.OrderBy(t => t));
        Constants(typeof(AdminEventTypes)).Count().ShouldBe(12);
    }

    [Theory]
    [MemberData(nameof(EveryEventType))]
    public void Every_event_type_has_its_own_sentence_from_its_payload(string type)
    {
        var (payload, expected) = Samples[type];

        AdminEventSummaryFactory.Summarize(type, payload).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(EveryEventType))]
    public void Every_event_type_has_a_plain_sentence_with_an_empty_payload_too(string type)
    {
        var summary = AdminEventSummaryFactory.Summarize(type, "{}");

        summary.ShouldNotBeNullOrWhiteSpace();
        summary.ShouldNotContain("{");
    }

    [Theory]
    [MemberData(nameof(EveryEventType))]
    public void No_payload_shape_makes_any_event_type_throw_or_echo_the_payload(string type)
    {
        string[] payloads =
        [
            string.Empty, "   ", "not json", "[]", "null", "42", "\"text\"", "{", "{\"slug\":42}", "{\"slug\":{\"a\":1}}", "{\"slug\":null,\"kind\":[]}",
            "{\"detachedTicketCount\":\"x\"}", "{\"detachedTicketCount\":99999999999}", "{\"detachedTicketCount\":-3}", "{\"detachedTicketCount\":1.5}",
            "{\"changed\":\"name\"}", "{\"changed\":[1,null,{},[]]}", "{\"isActive\":\"yes\"}", "{\"attempts\":true}",
            "{\"hostile\":\"<script>alert(1)</script>\"}",
        ];

        foreach (var payload in payloads)
        {
            var summary = AdminEventSummaryFactory.Summarize(type, payload);

            summary.ShouldNotBeNullOrWhiteSpace(payload);
            summary.ShouldNotContain("<script>", Shouldly.Case.Insensitive, payload);
            summary.ShouldNotContain("hostile", Shouldly.Case.Insensitive, payload);
            summary.ShouldNotContain("{", Shouldly.Case.Sensitive, payload);
        }
    }

    [Fact]
    public void An_agent_update_says_activated_deactivated_or_just_changed()
    {
        AdminEventSummaryFactory.Summarize(AdminEventTypes.AgentUpdated, "{\"isActive\":true}").ShouldBe("Activated an agent");
        AdminEventSummaryFactory.Summarize(AdminEventTypes.AgentUpdated, "{\"isActive\":false}").ShouldBe("Deactivated an agent");
        AdminEventSummaryFactory.Summarize(AdminEventTypes.AgentUpdated, "{}").ShouldBe("Changed an agent");
    }

    [Fact]
    public void A_value_from_the_payload_is_shortened_and_has_no_control_characters()
    {
        var summary = AdminEventSummaryFactory.Summarize(AdminEventTypes.TagCreated, "{\"slug\":\"" + new string('a', 300) + "\"}");

        summary.Length.ShouldBeLessThan(80);
        summary.ShouldEndWith("\u2026");

        AdminEventSummaryFactory.Summarize(AdminEventTypes.TagCreated, "{\"slug\":\"a\\nb\\u0007c\"}").ShouldBe("Created tag a b c");
    }

    [Fact]
    public void Fields_the_factory_does_not_know_are_never_shown()
    {
        var summary = AdminEventSummaryFactory.Summarize(AdminEventTypes.TagCreated, "{\"slug\":\"bug\",\"secret\":\"hunter2\",\"email\":\"ada@example.com\"}");

        summary.ShouldBe("Created tag bug");
    }

    [Fact]
    public void An_unknown_event_type_is_shown_as_words_and_never_throws()
    {
        AdminEventSummaryFactory.Summarize("SomethingNew", "{\"slug\":\"x\"}").ShouldBe("Something new");
        AdminEventSummaryFactory.Summarize("WebhookSecretRotated", "not json").ShouldBe("Webhook secret rotated");
        AdminEventSummaryFactory.Summarize(string.Empty, string.Empty).ShouldBe("Admin event");
    }

    [Fact]
    public void Every_subject_type_has_its_own_label_and_an_unknown_one_is_shown_as_it_came()
    {
        var labels = Constants(typeof(AdminSubjectTypes)).Select(AdminEventSummaryFactory.SubjectLabel).ToList();

        labels.Count.ShouldBe(7);
        labels.Distinct().Count().ShouldBe(7);
        labels.ShouldAllBe(l => !string.IsNullOrWhiteSpace(l));
        AdminEventSummaryFactory.SubjectLabel(AdminSubjectTypes.EmailOutbox).ShouldBe("Email");
        AdminEventSummaryFactory.SubjectLabel("Webhook").ShouldBe("Webhook");
    }
}
