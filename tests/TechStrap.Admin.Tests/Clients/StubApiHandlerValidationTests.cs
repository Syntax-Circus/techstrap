using System.Net;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Tags;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>
/// <see cref="StubApiHandler.ValidationProblem(string, string, string)"/> must produce what the API produces, or every form test that uses it would pass against a
/// shape the real API never sends. Read through the real ProblemMapping, it yields one error per field and code with the kebab-case target.
/// </summary>
public sealed class StubApiHandlerValidationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_validation_problem_reaches_the_client_as_a_field_error_with_the_code_and_message()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnValidationProblem(HttpMethod.Post, "/api/tags", ApiFields.Colour, "colour-invalid", "color must be a #RRGGBB color.");

        var result = await api.Get<ITagsClient>().CreateAsync(new CreateTagRequest("bug", "Bug", "red"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError("colour-invalid", "color must be a #RRGGBB color.", ResultErrorKind.Validation, "colour"));
    }

    [Fact]
    public async Task Several_errors_keep_their_fields_and_an_empty_target_is_a_form_level_error()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.On(HttpMethod.Post, "/api/tags", _ => StubApiHandler.ValidationProblem(
        [
            ("slug", "slug-invalid", "slug must be lower-case."),
            ("name", "name-required", "name is required."),
            ("name", "name-too-long", "name is too long."),
            ("", "form-invalid", "Fix the form."),
        ]));

        var result = await api.Get<ITagsClient>().CreateAsync(new CreateTagRequest(null, null, null), Ct);

        result.Errors.Select(e => (e.Target, e.Code)).ShouldBe(
            [("slug", "slug-invalid"), ("name", "name-required"), ("name", "name-too-long"), (null, "form-invalid")], ignoreOrder: true);
        result.Errors.ShouldAllBe(e => e.Kind == ResultErrorKind.Validation);
    }

    [Fact]
    public async Task The_answer_is_a_400_problem_json_with_errors_and_errorCodes_keyed_by_the_kebab_field()
    {
        using var response = StubApiHandler.ValidationProblem("logo-path", "logo-path-invalid", "logo-path must be an https URL.");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("\"type\":\"validation-failed\"");
        body.ShouldContain("\"errors\":{\"logo-path\":[\"logo-path must be an https URL.\"]}");
        body.ShouldContain("\"errorCodes\":{\"logo-path\":[\"logo-path-invalid\"]}");
    }
}
