using TechStrap.Contracts.Intake;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// P09-T06 and T21: the Portal's check of the contact form before it asks the API. Each rule at its boundary, with the Contracts constants (the same ones the inputs' <c>maxlength</c> attributes use), the API's own
/// codes, the Portal's own sentences, and values judged trimmed. Name is required (the UX brief) although the API only limits its length.
/// </summary>
public sealed class ContactFormValidatorTests
{
    private static ContactFormViewModel Valid() => new() { Name = "Ada Lovelace", Email = "ada@example.com", Subject = "Printer jam", Body = "It jams every time." };

    private static string[] Codes(ContactFormViewModel form) => [.. ContactFormValidator.Validate(form).Select(e => e.Code)];

    [Fact]
    public void A_valid_form_has_no_errors()
    {
        ContactFormValidator.Validate(Valid()).ShouldBeEmpty();
    }

    [Fact]
    public void A_form_with_nothing_in_it_fails_every_required_field_in_the_order_of_the_form()
    {
        var errors = ContactFormValidator.Validate(new ContactFormViewModel());

        errors.Select(e => (e.Field, e.Code)).ShouldBe(
        [
            (FormFields.Name, "name-required"),
            (FormFields.Email, "email-required"),
            (FormFields.Subject, "subject-required"),
            (FormFields.Body, "body-required"),
        ]);
        errors.ShouldAllBe(e => e.Message.Length > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void A_blank_value_is_a_missing_value(string blank)
    {
        var form = Valid();
        form.Name = blank;
        form.Email = blank;
        form.Subject = blank;
        form.Body = blank;

        Codes(form).ShouldBe(["name-required", "email-required", "subject-required", "body-required"]);
    }

    [Fact]
    public void Each_text_limit_is_inclusive_and_one_more_fails_with_the_apis_code()
    {
        var form = Valid();
        form.Name = new string('n', IntakeLimits.NameMaxLength);
        form.Subject = new string('s', IntakeLimits.SubjectMaxLength);
        form.Body = new string('b', IntakeLimits.BodyMaxLength);
        form.Email = new string('e', IntakeLimits.EmailMaxLength - "@example.com".Length) + "@example.com";
        ContactFormValidator.Validate(form).ShouldBeEmpty();

        form.Name += "n";
        form.Subject += "s";
        form.Body += "b";
        form.Email = "e" + form.Email;

        Codes(form).ShouldBe(["name-too-long", "email-invalid", "subject-too-long", "body-too-long"]);
    }

    [Fact]
    public void A_value_is_judged_after_trimming()
    {
        var form = Valid();
        form.Subject = "  " + new string('s', IntakeLimits.SubjectMaxLength) + "  ";
        form.Email = "  ada@example.com  ";

        ContactFormValidator.Validate(form).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ada")]
    [InlineData("ada@")]
    [InlineData("@example.com")]
    [InlineData("ada@example")]
    [InlineData("ada example@example.com")]
    [InlineData("ada@example.com, bob@example.com")]
    [InlineData("Ada <ada@example.com>")]
    [InlineData("ada@@example.com")]
    [InlineData("jane@")]
    public void An_email_that_is_not_one_plain_dotted_address_is_invalid(string email)
    {
        var form = Valid();
        form.Email = email;

        var error = ContactFormValidator.Validate(form).ShouldHaveSingleItem();

        error.Field.ShouldBe(FormFields.Email);
        error.Code.ShouldBe("email-invalid");
        error.Message.ShouldBe("Enter a valid email address, like name@example.com.");
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("ada.lovelace+support@sub.example.co.uk")]
    [InlineData("a@b.io")]
    public void A_plain_address_is_valid(string email)
    {
        var form = Valid();
        form.Email = email;

        ContactFormValidator.Validate(form).ShouldBeEmpty();
    }

    [Fact]
    public void The_honeypot_is_never_a_validation_error()
    {
        var form = Valid();
        form.Website = "http://spam.example";

        ContactFormValidator.Validate(form).ShouldBeEmpty();
    }

    [Fact]
    public void The_attachments_are_checked_too_and_their_errors_come_last()
    {
        var form = Valid();
        form.Subject = "";
        form.Files = [new FakeBrowserFile("virus.exe", 10)];

        Codes(form).ShouldBe(["subject-required", "attachment-type-not-allowed"]);
    }

    [Fact]
    public void A_prefill_becomes_the_three_inputs_and_nothing_else()
    {
        var form = ContactFormViewModel.FromPrefill("Printer jam", "Jane Doe", "jane@example.com");

        form.Subject.ShouldBe("Printer jam");
        form.Name.ShouldBe("Jane Doe");
        form.Email.ShouldBe("jane@example.com");
        form.Body.ShouldBeNull();
        form.Website.ShouldBeNull("the honeypot is never prefilled");
        form.Files.ShouldBeNull();
    }

    [Fact]
    public void A_prefill_is_judged_exactly_like_typed_text()
    {
        var typed = new ContactFormViewModel { Name = "Jane Doe", Email = "jane@", Subject = new string('s', 201), Body = "x" };
        var prefilled = ContactFormViewModel.FromPrefill(typed.Subject, typed.Name, typed.Email);
        prefilled.Body = "x";

        ContactFormValidator.Validate(prefilled).ShouldBe(ContactFormValidator.Validate(typed));
    }
}
