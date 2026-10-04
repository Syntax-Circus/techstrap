using System.Reflection;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TimelineEntryFactoryTests
{
    private static readonly Guid OtherProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid MessageId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static readonly TicketLookups Lookups = new(
        [TestData.Product("Orbitly"), TestData.Product("Nimbus", OtherProductId)],
        [TestData.Agent("Sam Ortiz", TestData.SamAgentId), TestData.Agent("Ada Admin", TestData.AdaAgentId)],
        [TestData.Tag("bug")]);

    /// <summary>One plausible payload per event type, as the Domain writes them (Ticket.cs). A new constant without a sample fails the coverage test below.</summary>
    private static readonly Dictionary<string, (string Payload, string Expected)> Samples = new()
    {
        [TicketEventTypes.Created] = ("""{"number":"ORB-42","channel":"Email"}""", "Ticket opened via Email"),
        [TicketEventTypes.MessageAdded] = ($$"""{"messageId":"{{Guid.NewGuid()}}","visibility":"Public"}""", "A message was added"),
        [TicketEventTypes.StatusChanged] = ("""{"from":"Open","to":"Pending"}""", "Status changed from Open to Pending"),
        [TicketEventTypes.Assigned] = ($$"""{"from":null,"to":"{{TestData.SamAgentId}}"}""", "Assigned to Sam Ortiz"),
        [TicketEventTypes.ProductChanged] = ($$"""{"from":"{{TestData.OrbitlyId}}","to":"{{OtherProductId}}"}""", "Product changed from Orbitly to Nimbus"),
        [TicketEventTypes.PriorityChanged] = ("""{"from":"Normal","to":"Urgent"}""", "Priority changed from Normal to Urgent"),
        [TicketEventTypes.TagAdded] = ($$"""{"tagId":"{{TestData.BugTagId}}"}""", "Tag bug added"),
        [TicketEventTypes.TagRemoved] = ($$"""{"tagId":"{{TestData.BugTagId}}"}""", "Tag bug removed"),
        [TicketEventTypes.MarkedSpam] = ("""{"isSpam":true}""", "Marked as spam"),
        [TicketEventTypes.FollowUpCreated] = ("""{"followUpTicketId":"ffffffff-0000-0000-0000-000000000001"}""", "The customer replied after the ticket closed, so a follow-up ticket was opened"),
    };

    private static IReadOnlyList<string> EveryEventTypeConstant() =>
        typeof(TicketEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

    public static TheoryData<string> EventTypes()
    {
        var data = new TheoryData<string>();
        foreach (var type in EveryEventTypeConstant())
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void Every_event_type_constant_has_a_sample_here_so_a_new_type_cannot_slip_past_the_timeline()
    {
        EveryEventTypeConstant().ShouldBe(Samples.Keys, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void Every_event_type_becomes_one_readable_entry_and_never_throws(string type)
    {
        var (payload, expected) = Samples[type];

        var entries = TimelineEntryFactory.Build([], [TestData.Event(type, payload)], Lookups);

        var entry = entries.ShouldHaveSingleItem();
        entry.Kind.ShouldBe(TimelineEntryKind.Event);
        entry.Text.ShouldBe(expected);
        entry.Actor.ShouldBe("Sam Ortiz");
    }

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void An_unreadable_payload_still_gives_a_line_for_every_type(string type)
    {
        foreach (var payload in new[] { "", "not json", "[1,2]", "null", "{}", """{"from":5,"to":{}}""" })
        {
            var entries = TimelineEntryFactory.Build([], [TestData.Event(type, payload)], Lookups);

            entries.ShouldHaveSingleItem().Text.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void An_event_type_this_build_does_not_know_is_a_generic_line()
    {
        var entries = TimelineEntryFactory.Build([], [TestData.Event("SomethingNew", "{}")], Lookups);

        entries.ShouldHaveSingleItem().Text.ShouldBe("Event: SomethingNew");
    }

    [Fact]
    public void A_MessageAdded_event_is_folded_into_its_message_so_a_reply_appears_once()
    {
        var message = TestData.Message(MessageAuthorTypes.Agent, id: MessageId, authorName: "Sam Ortiz");
        var folded = TestData.Event(TicketEventTypes.MessageAdded, $$"""{"messageId":"{{MessageId}}","visibility":"Public"}""");
        var orphan = TestData.Event(TicketEventTypes.MessageAdded, $$"""{"messageId":"{{Guid.NewGuid()}}","visibility":"Public"}""");

        var entries = TimelineEntryFactory.Build([message], [folded, orphan], Lookups);

        entries.Count(e => e.Kind == TimelineEntryKind.Message).ShouldBe(1);
        entries.Count(e => e.Text == "A message was added").ShouldBe(1);
    }

    [Theory]
    [InlineData(MessageAuthorTypes.Requester, MessageVisibilities.Public, EntryKind.Customer)]
    [InlineData(MessageAuthorTypes.Agent, MessageVisibilities.Public, EntryKind.PublicReply)]
    [InlineData(MessageAuthorTypes.Agent, MessageVisibilities.Internal, EntryKind.InternalNote)]
    [InlineData(MessageAuthorTypes.System, MessageVisibilities.Public, EntryKind.PublicReply)]
    public void A_message_gets_the_tint_of_who_wrote_it_and_who_may_see_it(string author, string visibility, EntryKind expected)
    {
        var entries = TimelineEntryFactory.Build([TestData.Message(author, visibility)], [], Lookups);

        entries.Single().Message!.Kind.ShouldBe(expected);
    }

    [Fact]
    public void Entries_are_oldest_first_with_Created_before_the_first_message_and_changes_after_it()
    {
        var t = TestData.Now.AddHours(-5);
        var created = TestData.Event(TicketEventTypes.Created, """{"channel":"Email"}""", t, "Requester", "Ada Lovelace");
        var first = TestData.Message(at: t);
        var reply = TestData.Message(MessageAuthorTypes.Agent, at: t.AddHours(1), authorName: "Sam Ortiz");
        var status = TestData.Event(TicketEventTypes.StatusChanged, """{"from":"New","to":"Pending"}""", t.AddHours(1));
        var late = TestData.Event(TicketEventTypes.PriorityChanged, """{"from":"Normal","to":"High"}""", t.AddHours(2));

        var entries = TimelineEntryFactory.Build([first, reply], [created, status, late], Lookups);

        entries.Select(e => e.Kind == TimelineEntryKind.Message ? "message:" + e.Actor : e.Text).ShouldBe(
        [
            "Ticket opened via Email",
            "message:Ada Lovelace",
            "message:Sam Ortiz",
            "Status changed from New to Pending",
            "Priority changed from Normal to High",
        ]);
    }

    [Theory]
    [InlineData(null, "ada", "Assigned to Ada Admin")]
    [InlineData("sam", null, "Unassigned (was Sam Ortiz)")]
    [InlineData("sam", "ada", "Reassigned from Sam Ortiz to Ada Admin")]
    [InlineData(null, null, "Assignment changed")]
    public void Assignments_say_who_got_it_and_who_lost_it(string? from, string? to, string expected)
    {
        static string Json(string? who) => who switch { "sam" => $"\"{TestData.SamAgentId}\"", "ada" => $"\"{TestData.AdaAgentId}\"", _ => "null" };

        var entries = TimelineEntryFactory.Build([], [TestData.Event(TicketEventTypes.Assigned, $$"""{"from":{{Json(from)}},"to":{{Json(to)}}}""")], Lookups);

        entries.Single().Text.ShouldBe(expected);
    }

    [Fact]
    public void An_id_the_lookups_do_not_know_gets_a_plain_fallback_not_a_guid()
    {
        var unknown = Guid.NewGuid();

        var entries = TimelineEntryFactory.Build([],
        [
            TestData.Event(TicketEventTypes.Assigned, $$"""{"from":null,"to":"{{unknown}}"}"""),
            TestData.Event(TicketEventTypes.TagAdded, $$"""{"tagId":"{{unknown}}"}"""),
            TestData.Event(TicketEventTypes.ProductChanged, $$"""{"from":"{{unknown}}","to":"{{TestData.OrbitlyId}}"}"""),
        ], Lookups);

        entries.Select(e => e.Text).ShouldBe(
            ["Assigned to another agent", "Tag a deleted tag added", "Product changed from another product to Orbitly"]);
        entries.ShouldNotContain(e => e.Text.Contains(unknown.ToString()));
    }

    [Fact]
    public void A_tag_removed_because_the_tag_was_deleted_says_so_and_a_restore_from_spam_is_not_called_spam()
    {
        var entries = TimelineEntryFactory.Build([],
        [
            TestData.Event(TicketEventTypes.TagRemoved, $$"""{"tagId":"{{TestData.BugTagId}}","reason":"tag-deleted"}"""),
            TestData.Event(TicketEventTypes.MarkedSpam, """{"isSpam":false}"""),
        ], Lookups);

        entries.Select(e => e.Text).ShouldBe(["Tag bug removed because the tag was deleted", "Restored from spam"]);
    }

    [Fact]
    public void A_system_actor_such_as_auto_close_is_named_as_system()
    {
        var entries = TimelineEntryFactory.Build([],
            [TestData.Event(TicketEventTypes.StatusChanged, """{"from":"Solved","to":"Closed"}""", actorType: "System", actorName: null)], Lookups);

        entries.Single().Actor.ShouldBe("System");
    }
}
