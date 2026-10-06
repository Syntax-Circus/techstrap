using TechStrap.Contracts.Intake;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>The checks the reply form and the lost-link form make before they ask the API, each at its boundary, with the same codes and sentences as the contact form.</summary>
public sealed class ReplyAndEmailRulesTests
{
    [Fact]
    public void A_reply_with_a_body_and_no_files_is_valid()
    {
        ReplyFormValidator.Validate(new ReplyFormViewModel { Body = "Still broken." }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    public void A_blank_body_is_required(string? body)
    {
        var error = ReplyFormValidator.Validate(new ReplyFormViewModel { Body = body }).ShouldHaveSingleItem();

        error.Field.ShouldBe(FormFields.Body);
        error.Code.ShouldBe("body-required");
        error.Message.ShouldBe("Write a message.");
    }

    [Fact]
    public void The_body_limit_is_inclusive_and_judged_after_trimming()
    {
        ReplyFormValidator.Validate(new ReplyFormViewModel { Body = "  " + new string('b', IntakeLimits.BodyMaxLength) + "  " }).ShouldBeEmpty();
        ReplyFormValidator.Validate(new ReplyFormViewModel { Body = new string('b', IntakeLimits.BodyMaxLength + 1) }).ShouldHaveSingleItem().Code.ShouldBe("body-too-long");
    }

    [Fact]
    public void The_files_of_a_reply_are_checked_like_the_contact_forms()
    {
        var codes = ReplyFormValidator.Validate(new ReplyFormViewModel { Body = "x", Files = [new FakeBrowserFile("virus.exe", 5), new FakeBrowserFile("empty.txt", 0)] }).Select(e => e.Code);

        codes.ShouldBe(["attachment-type-not-allowed", "attachment-empty"]);
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("  ada@example.com  ")]
    [InlineData("a.b+c@sub.example.co.uk")]
    public void A_plain_dotted_address_is_acceptable(string email)
    {
        EmailRules.Check(email).ShouldBeNull();
    }

    [Theory]
    [InlineData(null, "email-required")]
    [InlineData("", "email-required")]
    [InlineData("   ", "email-required")]
    [InlineData("ada", "email-invalid")]
    [InlineData("ada@example", "email-invalid")]
    [InlineData("Ada <ada@example.com>", "email-invalid")]
    [InlineData("a@b.example, c@d.example", "email-invalid")]
    public void A_missing_or_malformed_address_is_an_error_on_the_email_field(string? email, string code)
    {
        var error = EmailRules.Check(email).ShouldNotBeNull();

        error.Field.ShouldBe(FormFields.Email);
        error.Code.ShouldBe(code);
        error.Message.ShouldBe(FormCopy.For(code));
    }

    [Fact]
    public void The_email_limit_is_inclusive()
    {
        EmailRules.Check(new string('e', IntakeLimits.EmailMaxLength - "@example.com".Length) + "@example.com").ShouldBeNull();
        EmailRules.Check("e" + new string('e', IntakeLimits.EmailMaxLength - "@example.com".Length) + "@example.com").ShouldNotBeNull().Code.ShouldBe("email-invalid");
    }
}
