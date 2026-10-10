using System.Buffers;

namespace TechStrap.Infrastructure.Attachments;

/// <summary>An uploaded image read into memory: the bytes and the extension <see cref="KbImageSignatures.Identify"/> gives them (null when they are not an accepted image).</summary>
internal sealed record CappedImage(MemoryStream Content, string? Extension);

/// <summary>
/// The capped read shared by the KB image store and the product logo store (D-044, D-052): refuses a declared length over the cap, then reads at most cap + 1 bytes so a declared length that lies
/// cannot exhaust memory, and returns null when the real length is over the cap too. The caller decides the error code and which extensions it accepts.
/// </summary>
internal static class CappedImageIntake
{
    public static async Task<CappedImage?> ReadAsync(Stream content, long declaredLength, long maxBytes, CancellationToken cancellationToken)
    {
        if (declaredLength > maxBytes)
        {
            return null;
        }

        var copy = new MemoryStream((int)Math.Clamp(declaredLength, 0, maxBytes));
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            long total = 0;
            while (total <= maxBytes)
            {
                var want = (int)Math.Min(buffer.Length, maxBytes + 1 - total);
                var read = await content.ReadAsync(buffer.AsMemory(0, want), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                copy.Write(buffer, 0, read);
                total += read;
            }
        }
        catch
        {
            await copy.DisposeAsync();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (copy.Length > maxBytes)
        {
            await copy.DisposeAsync();
            return null;
        }

        copy.Position = 0;
        return new CappedImage(copy, KbImageSignatures.Identify(copy.GetBuffer().AsSpan(0, (int)copy.Length)));
    }
}
