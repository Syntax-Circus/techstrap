using Microsoft.AspNetCore.Components.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>A file as the form binder hands it over: a name, a declared size and a content type. Reading it with a limit below its size throws, as the framework's does.</summary>
internal sealed class FakeBrowserFile(string name, long size, string contentType = "text/plain", byte[]? content = null) : IBrowserFile
{
    public int Opened { get; private set; }

    public long? LastLimit { get; private set; }

    public string Name => name;

    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

    public long Size => size;

    public string ContentType => contentType;

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        Opened++;
        LastLimit = maxAllowedSize;
        if (size > maxAllowedSize)
        {
            throw new IOException($"Supplied file with size {size} bytes exceeds the maximum of {maxAllowedSize} bytes.");
        }

        return new MemoryStream(content ?? new byte[size]);
    }
}
