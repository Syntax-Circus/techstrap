using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class AttachmentTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    private DomainResult<Attachment> Create(string name, string type) =>
        Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), name, type, 10, "key", _clock);

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("invoice‮fdp.exe")]
    [InlineData("a​b.txt")]
    public void A_dot_name_or_a_name_with_format_characters_is_rejected(string name)
    {
        Create(name, "text/plain").Error!.Code.ShouldBe("file-name-invalid");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("text/")]
    [InlineData("/plain")]
    [InlineData("a/b\r\nX: y")]
    [InlineData("text/plain; x=1")]
    [InlineData(" text/plain")]
    [InlineData("text/pl ain")]
    [InlineData("a/b/c")]
    public void A_content_type_that_is_not_a_bare_type_slash_subtype_is_rejected(string type)
    {
        Create("a.txt", type).Error!.Code.ShouldBe("content-type-invalid");
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("image/svg+xml")]
    [InlineData("application/x-tar_gz")]
    public void A_valid_content_type_and_name_pass(string type)
    {
        Create("report.v2.pdf", type).IsSuccess.ShouldBeTrue();
    }
}
