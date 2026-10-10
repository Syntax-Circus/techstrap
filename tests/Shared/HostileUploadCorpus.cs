using System.Text.Json;

namespace TechStrap.Tests.Shared;

/// <summary>What a store or the intake endpoint must answer for one hostile upload.</summary>
/// <param name="Stored">True when the upload is accepted.</param>
/// <param name="ErrorCode">The error code of a refusal.</param>
/// <param name="StoredName">The exact display name an accepted attachment is stored under.</param>
/// <param name="StoredNameRule">A rule (<c>maxLength:N,suffix:S</c>) for a name that cannot be written out exactly.</param>
/// <param name="ContentType">The content type an accepted attachment is stored with, when the manifest pins it.</param>
public sealed record Expectation(bool Stored, string? ErrorCode, string? StoredName, string? StoredNameRule, string? ContentType)
{
    /// <summary>True when <paramref name="name"/> is the name this expectation records.</summary>
    public bool NameMatches(string name)
    {
        if (StoredName is not null)
        {
            return name == StoredName;
        }

        if (StoredNameRule is null)
        {
            return true;
        }

        var max = 0;
        var suffix = string.Empty;
        foreach (var part in StoredNameRule.Split(','))
        {
            var pair = part.Split(':', 2);
            if (pair[0] == "maxLength")
            {
                max = int.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (pair[0] == "suffix")
            {
                suffix = pair[1];
            }
        }

        return name.Length <= max && name.EndsWith(suffix, StringComparison.Ordinal);
    }
}

/// <summary>One hostile upload: the file name and declared type a client sends, the bytes, and the outcome each consumer must give.</summary>
public sealed record HostileUpload(string Id, string FileName, string? DeclaredContentType, byte[] Content, Expectation Attachment, Expectation KbImage, Expectation ProductLogo);

/// <summary>
/// The hostile-upload corpus (Fixtures/hostile-uploads/manifest.json, copied beside the test assembly), shared by the attachment store, the KB image store and the
/// public intake endpoint tests so the three are held to one list.
/// </summary>
public static class HostileUploadCorpus
{
    private static readonly Lazy<IReadOnlyList<HostileUpload>> _all = new(Load);

    public static IReadOnlyList<HostileUpload> All => _all.Value;

    public static IEnumerable<TheoryDataRow<string>> Rows() => All.Select(upload => new TheoryDataRow<string>(upload.Id) { TestDisplayName = upload.Id });

    public static HostileUpload Get(string id) => All.Single(upload => upload.Id == id);

    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hostile-uploads");

    private static IReadOnlyList<HostileUpload> Load()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Directory, "manifest.json")));
        return [.. manifest.RootElement.EnumerateArray().Select(Read)];
    }

    private static HostileUpload Read(JsonElement entry) => new(
        entry.GetProperty("id").GetString()!,
        entry.GetProperty("fileName").GetString()!,
        entry.TryGetProperty("declaredContentType", out var declared) ? declared.GetString() : null,
        Content(entry.GetProperty("content")),
        Expect(entry.GetProperty("attachment")),
        Expect(entry.GetProperty("kbImage")),
        Expect(entry.GetProperty("productLogo")));

    private static byte[] Content(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return File.ReadAllBytes(Path.Combine(Directory, content.GetString()!));
        }

        var generate = content.GetProperty("generate");
        var bytes = new byte[generate.GetProperty("totalBytes").GetInt32()];
        Convert.FromHexString(generate.GetProperty("prefixHex").GetString()!).CopyTo(bytes, 0);
        return bytes;
    }

    private static Expectation Expect(JsonElement element) => new(
        element.GetProperty("stored").GetBoolean(),
        Text(element, "errorCode"),
        Text(element, "storedName"),
        Text(element, "storedNameRule"),
        Text(element, "contentType"));

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() : null;
}
