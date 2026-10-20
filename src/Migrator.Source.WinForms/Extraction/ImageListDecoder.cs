namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Decodes the `ImageStream` resource of a WinForms ImageList in managed code (no System.Drawing, no Windows API). The resource is a BinaryFormatter
/// `ImageListStreamer` whose byte array starts with the signature `MSFt` followed by run-length pairs (count, value). Expanded, it is the native image list
/// stream: a 28-byte header (`IL`, version, image count, cell size, ...), the strip of all images as one DIB (cells in rows, 1, 4, 8, 24 or 32 bits per pixel),
/// and for lists without an alpha channel a second 1-bit DIB that marks transparent pixels. Each image is returned as a standalone 32-bit BMP with alpha, which
/// the target turns into a PNG. Anything that does not match (other colour depths, truncated data) yields null, and the caller keeps a placeholder.
/// </summary>
internal static class ImageListDecoder
{
    public sealed record Decoded(int Width, int Height, List<byte[]> Images);

    private const int MaxImages = 1024;
    private const int HeaderSize = 28;

    public static Decoded? Decode(byte[] serialized)
    {
        try
        {
            return DecodeCore(serialized);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or OverflowException or InvalidDataException)
        {
            return null; // malformed stream: no images are extracted
        }
    }

    private static Decoded? DecodeCore(byte[] serialized)
    {
        var at = serialized.AsSpan().IndexOf("MSFt"u8);
        if (at < 0) return null;
        var data = Expand(serialized, at + 4);
        if (data.Length < HeaderSize + 54 || data[0] != (byte)'I' || data[1] != (byte)'L') return null;

        int count = U16(data, 4), cellWidth = U16(data, 10), cellHeight = U16(data, 12);
        if (count <= 0 || count > MaxImages || cellWidth <= 0 || cellHeight <= 0) return null;

        var strip = ReadDib(data, HeaderSize, out var next);
        if (strip is null || strip.Width < cellWidth) return null;
        var mask = next + 14 < data.Length && data[next] == (byte)'B' && data[next + 1] == (byte)'M' ? ReadDib(data, next, out _) : null;
        if (mask is not null && (mask.Width != strip.Width || mask.Height != strip.Height)) mask = null;

        var perRow = strip.Width / cellWidth;
        var images = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            var x0 = i % perRow * cellWidth;
            var y0 = i / perRow * cellHeight;
            if (y0 + cellHeight > strip.Height) break;
            images.Add(Cell(strip, mask, x0, y0, cellWidth, cellHeight));
        }
        return images.Count == 0 ? null : new Decoded(cellWidth, cellHeight, images);
    }

    /// <summary>Run-length pairs (count, value) after the signature; a trailing odd byte (the serializer's end marker) is ignored.</summary>
    private static byte[] Expand(byte[] source, int start)
    {
        using var output = new MemoryStream();
        for (var k = start; k + 1 < source.Length; k += 2)
            for (var n = 0; n < source[k]; n++) output.WriteByte(source[k + 1]);
        return output.ToArray();
    }

    private static int U16(byte[] d, int at) => d[at] | d[at + 1] << 8;

    private sealed record Dib(int Width, int Height, byte[] Rgba, bool HasAlpha);

    /// <summary>One uncompressed DIB with its file header at <paramref name="start"/>, as pixels top-down (RGBA); <paramref name="end"/> is where its pixel data ends.</summary>
    private static Dib? ReadDib(byte[] d, int start, out int end)
    {
        end = start;
        if (d[start] != (byte)'B' || d[start + 1] != (byte)'M') return null;
        var pixelsAt = start + BitConverter.ToInt32(d, start + 10);
        var info = start + 14;
        var headerSize = BitConverter.ToInt32(d, info);
        var width = BitConverter.ToInt32(d, info + 4);
        var rawHeight = BitConverter.ToInt32(d, info + 8);
        var bits = BitConverter.ToUInt16(d, info + 14);
        var compression = BitConverter.ToInt32(d, info + 16);
        var colors = BitConverter.ToInt32(d, info + 32);
        if (headerSize < 40 || compression is not (0 or 3) || width <= 0 || width > 4096 || rawHeight == 0 || Math.Abs(rawHeight) > 65536 || bits is not (1 or 4 or 8 or 24 or 32))
            return null;

        var topDown = rawHeight < 0;
        var height = Math.Abs(rawHeight);
        var paletteAt = info + headerSize;
        var stride = (width * bits + 31) / 32 * 4;
        end = pixelsAt + stride * height;
        if (end > d.Length || pixelsAt < paletteAt) return null;

        var rgba = new byte[width * height * 4];
        var hasAlpha = false;
        for (var y = 0; y < height; y++)
        {
            var row = pixelsAt + (topDown ? y : height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;
                switch (bits)
                {
                    case 32: b = d[row + x * 4]; g = d[row + x * 4 + 1]; r = d[row + x * 4 + 2]; a = d[row + x * 4 + 3]; hasAlpha |= a != 0; break;
                    case 24: b = d[row + x * 3]; g = d[row + x * 3 + 1]; r = d[row + x * 3 + 2]; break;
                    default:
                        var index = bits == 8 ? d[row + x] : bits == 4 ? (d[row + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0xF : (d[row + x / 8] >> (7 - x % 8)) & 1;
                        var p = paletteAt + index * 4;
                        if (index >= (colors > 0 ? colors : 1 << bits) || p + 3 >= pixelsAt) { b = g = r = 0; break; }
                        b = d[p]; g = d[p + 1]; r = d[p + 2];
                        break;
                }
                var o = (y * width + x) * 4;
                rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = a;
            }
        }
        return new Dib(width, height, rgba, hasAlpha);
    }

    /// <summary>One cell of the strip as a standalone top-down 32-bit BMP (BGRA). Alpha comes from the strip when it has any, else from the mask (white = transparent).</summary>
    private static byte[] Cell(Dib strip, Dib? mask, int x0, int y0, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var from = ((y0 + y) * strip.Width + x0 + x) * 4;
                var to = (y * width + x) * 4;
                pixels[to] = strip.Rgba[from + 2]; pixels[to + 1] = strip.Rgba[from + 1]; pixels[to + 2] = strip.Rgba[from];
                pixels[to + 3] = strip.HasAlpha ? strip.Rgba[from + 3]
                    : mask is not null && mask.Rgba[from] > 127 ? (byte)0 : (byte)255;
            }

        var file = new byte[54 + pixels.Length];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(54).CopyTo(file, 10);
        BitConverter.GetBytes(40).CopyTo(file, 14);
        BitConverter.GetBytes(width).CopyTo(file, 18);
        BitConverter.GetBytes(-height).CopyTo(file, 22);
        BitConverter.GetBytes((short)1).CopyTo(file, 26);
        BitConverter.GetBytes((short)32).CopyTo(file, 28);
        BitConverter.GetBytes(pixels.Length).CopyTo(file, 34);
        pixels.CopyTo(file, 54);
        return file;
    }
}
