using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>The sentences of the forms: every code the API can send for a ticket or a reply has a sentence of the Portal's own, built from the Contracts limits, and nothing else leaks through.</summary>
public sealed class FormCopyTests
{
    private static readonly string[] ApiCodes =
    [
        "email-required", "email-invalid", "subject-required", "subject-too-long", "body-required", "body-too-long", "name-too-long",
        "attachments-too-many", "attachments-too-large", "attachment-too-large", "attachment-empty", "attachment-type-not-allowed",
    ];

    [Fact]
    public void Every_code_the_api_sends_for_a_ticket_or_a_reply_has_a_sentence_of_its_own()
    {
        foreach (var code in ApiCodes.Append(FormCopy.NameRequiredCode))
        {
            var sentence = FormCopy.For(code);
            sentence.ShouldNotBe(ProblemCopy.Invalid, $"{code} fell through to the generic sentence");
            sentence.ShouldNotBeNullOrWhiteSpace();
            sentence.ShouldNotContain("<");
            sentence.ShouldNotContain("API", Case.Sensitive, "the visitor does not know there is one");
        }
    }

    [Theory]
    [InlineData("name-too-long", "Your name must be at most 100 characters.")]
    [InlineData("subject-too-long", "The subject must be at most 200 characters.")]
    [InlineData("body-too-long", "The message must be at most 100,000 characters.")]
    [InlineData("attachments-too-many", "Attach at most 5 files.")]
    [InlineData("attachment-too-large", "A file is over 10 MB. Send a smaller one.")]
    [InlineData("attachments-too-large", "Your files add up to more than 25 MB. Remove a file or send smaller ones.")]
    public void The_limits_in_the_sentences_are_the_contract_limits(string code, string expected) => FormCopy.For(code).ShouldBe(expected);

    [Fact]
    public void An_unknown_code_is_the_generic_sentence()
    {
        FormCopy.For("something-new").ShouldBe(ProblemCopy.Invalid);
        FormCopy.For(string.Empty).ShouldBe(ProblemCopy.Invalid);
    }

    [Fact]
    public void The_attachment_rule_is_stated_with_the_size_count_and_every_allowed_type()
    {
        FormCopy.AttachmentRules.ShouldBe("Up to 5 files: images and documents of 10 MB each and 25 MB in all (.png, .jpg, .jpeg, .gif, .webp, .pdf, .txt, .log, .csv, .zip).");
        FormCopy.Accept.ShouldBe(".png,.jpg,.jpeg,.gif,.webp,.pdf,.txt,.log,.csv,.zip");
    }

    [Fact]
    public void The_notices_keep_what_the_visitor_wrote_and_say_what_to_do()
    {
        FormCopy.RateLimited.ShouldContain("still here");
        FormCopy.Unavailable.ShouldContain("still here");
        FormCopy.Unavailable.ShouldNotContain("API", Case.Sensitive);
        FormCopy.AttachmentsKept.ShouldContain("choose them again");
    }

    [Theory]
    [InlineData("name", "name-error")]
    [InlineData("attachments", "attachments-error")]
    public void An_error_paragraph_id_is_the_field_and_a_suffix(string field, string id) => FormFields.ErrorId(field).ShouldBe(id);

    [Theory]
    [InlineData("email", "email")]
    [InlineData("Email", "email")]
    [InlineData(" BODY ", "body")]
    [InlineData("attachments", "attachments")]
    [InlineData("Subject", "subject")]
    [InlineData("name", "name")]
    [InlineData("website", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Idempotency-Key", null)]
    public void An_api_target_maps_to_a_field_or_to_nothing(string? target, string? field) => FormFields.FromTarget(target).ShouldBe(field);
}
