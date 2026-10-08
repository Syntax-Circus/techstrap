using TechStrap.Tests.Shared;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class HostileUploadCorpusTests
{
    private static readonly string[] _classes =
    [
        "oversize", "double-extension", "png-named-pdf", "traversal-passwd", "traversal-text", "backslash-traversal",
        "rtl-override", "zero-byte", "svg-as-png", "html-as-txt", "long-name", "nul-in-name",
        "exe-with-png-bytes", "bat-with-text", "pdf-bytes-named-exe",
    ];

    [Fact]
    public void The_manifest_names_every_hostile_class_in_the_spec()
    {
        HostileUploadCorpus.All.Select(upload => upload.Id).ShouldBe(_classes, ignoreOrder: true);
        HostileUploadCorpus.Rows().Count().ShouldBe(_classes.Length);

        // Every file the manifest points at exists beside the test assembly.
        var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(HostileUploadCorpus.Directory, "manifest.json")));
        foreach (var entry in manifest.RootElement.EnumerateArray())
        {
            var content = entry.GetProperty("content");
            if (content.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                File.Exists(Path.Combine(HostileUploadCorpus.Directory, content.GetString()!)).ShouldBeTrue(content.GetString());
            }
        }
    }

    [Fact]
    public void The_hostile_file_names_survive_loading_exactly()
    {
        HostileUploadCorpus.Get("rtl-override").FileName.ShouldBe("invoice\u202Etxt.png");
        HostileUploadCorpus.Get("nul-in-name").FileName.ShouldBe("a\u0000b.txt");
        HostileUploadCorpus.Get("backslash-traversal").FileName.ShouldBe("..\\..\\x.txt");
        HostileUploadCorpus.Get("long-name").FileName.Length.ShouldBe(300);
        HostileUploadCorpus.Get("oversize").Content.Length.ShouldBe(10 * 1024 * 1024 + 1);
    }
}
