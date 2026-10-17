using System.IO.Compression;

namespace Migrator.Target.React.Generation;

/// <summary>
/// Turns image bytes taken from a designer resource into a file a browser can show, in managed code only (no System.Drawing, no Windows API):
/// PNG, GIF, JPEG and SVG pass through; ICO (PNG or DIB frames) and BMP are converted to PNG. Anything else, or anything malformed, is reported as not
/// convertible so the caller can use a placeholder.
/// </summary>
internal static class IconConverter
{
    public sealed record Result(byte[] Data, string Extension, bool Converted);

    private const int PreferredIconSize = 32;

    public static Result? ToWeb(byte[] bytes, string format)
    {
        try
        {
            switch (format)
            {
                case "png": return new Result(bytes, "png", false);
                case "gif": return new Result(bytes, "gif", false);
                case "jpg": return new Result(bytes, "jpg", false);
                case "svg": return new Result(bytes, "svg", false);
                case "bmp": return Dib(bytes, 14, isIcon: false) is { } bmp ? new Result(bmp, "png", true) : null;
                case "ico": return FromIcon(bytes);
                default: return null;
            }
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidDataException or OverflowException)
        {
            return null; // malformed image: the caller falls back to a placeholder
        }
    }

    // ------------------------------------------------------------------------------------------------ ICO

    private static Result? FromIcon(byte[] b)
    {
        var count = b[4] | b[5] << 8;
        (int Size, int Bytes, int Offset)? best = null;
        for (var i = 0; i < count && 6 + (i + 1) * 16 <= b.Length; i++)
        {
            var e = 6 + i * 16;
            var width = b[e] == 0 ? 256 : b[e];
            var bytes = BitConverter.ToInt32(b, e + 8);
            var offset = BitConverter.ToInt32(b, e + 12);
            if (offset < 0 || bytes <= 0 || offset + bytes > b.Length) continue;
            // Prefer the largest frame that is not bigger than 32 px; if every frame is bigger, the smallest one.
            var better = best is null
                || (width <= PreferredIconSize && (best.Value.Size > PreferredIconSize || width > best.Value.Size))
                || (width > PreferredIconSize && best.Value.Size > PreferredIconSize && width < best.Value.Size);
            if (better) best = (width, bytes, offset);
        }
        if (best is not { } frame) return null;

        if (frame.Bytes >= 8 && b[frame.Offset] == 0x89 && b[frame.Offset + 1] == 0x50) // a PNG frame
            return new Result(b.AsSpan(frame.Offset, frame.Bytes).ToArray(), "png", true);
        return Dib(b[frame.Offset..(frame.Offset + frame.Bytes)], 0, isIcon: true) is { } png ? new Result(png, "png", true) : null;
    }

    // ------------------------------------------------------------------------------------------------ DIB -> PNG

    /// <summary>Decodes an uncompressed DIB (1, 4, 8, 24 or 32 bits per pixel) starting at <paramref name="start"/>; an icon's height counts the AND mask too.</summary>
    private static byte[]? Dib(byte[] d, int start, bool isIcon)
    {
        var headerSize = BitConverter.ToInt32(d, start);
        if (headerSize < 40) return null;
        var width = BitConverter.ToInt32(d, start + 4);
        var rawHeight = BitConverter.ToInt32(d, start + 8);
        var bits = BitConverter.ToUInt16(d, start + 14);
        var compression = BitConverter.ToInt32(d, start + 16);
        var colors = BitConverter.ToInt32(d, start + 32);
        if (compression is not (0 or 3) || width <= 0 || width > 1024 || rawHeight == 0 || Math.Abs(rawHeight) > 2048) return null;
        if (bits is not (1 or 4 or 8 or 24 or 32)) return null;

        var topDown = rawHeight < 0;
        var height = isIcon ? Math.Abs(rawHeight) / 2 : Math.Abs(rawHeight);
        var paletteEntries = bits <= 8 ? (colors > 0 ? colors : 1 << bits) : 0;
        var paletteAt = start + headerSize;
        var pixelsAt = paletteAt + paletteEntries * 4;
        if (!isIcon && d.Length > 14 + 10 && start == 14) pixelsAt = BitConverter.ToInt32(d, 10); // the file header says where the pixels start

        var stride = (width * bits + 31) / 32 * 4;
        var maskStride = (width + 31) / 32 * 4;
        var maskAt = pixelsAt + stride * height;
        var rgba = new byte[width * height * 4];
        var hasAlpha = false;

        for (var y = 0; y < height; y++)
        {
            var row = pixelsAt + (topDown ? y : height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                byte r, g, bl, a = 255;
                switch (bits)
                {
                    case 32: bl = d[row + x * 4]; g = d[row + x * 4 + 1]; r = d[row + x * 4 + 2]; a = d[row + x * 4 + 3]; hasAlpha |= a != 0; break;
                    case 24: bl = d[row + x * 3]; g = d[row + x * 3 + 1]; r = d[row + x * 3 + 2]; break;
                    default:
                        var index = bits == 8 ? d[row + x] : bits == 4 ? (d[row + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0xF : (d[row + x / 8] >> (7 - x % 8)) & 1;
                        var p = paletteAt + index * 4;
                        bl = d[p]; g = d[p + 1]; r = d[p + 2];
                        break;
                }
                var o = (y * width + x) * 4;
                rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = bl; rgba[o + 3] = a;
            }
        }

        // 32-bit frames with no alpha at all, and every non-32-bit icon frame, take transparency from the AND mask.
        if (isIcon && (bits != 32 || !hasAlpha) && maskAt + maskStride * height <= d.Length)
            for (var y = 0; y < height; y++)
            {
                var row = maskAt + (topDown ? y : height - 1 - y) * maskStride;
                for (var x = 0; x < width; x++)
                    rgba[(y * width + x) * 4 + 3] = (byte)(((d[row + x / 8] >> (7 - x % 8)) & 1) == 1 ? 0 : 255);
            }
        else if (bits == 32 && !hasAlpha)
            for (var i = 3; i < rgba.Length; i += 4) rgba[i] = 255;

        return EncodePng(rgba, width, height);
    }

    // ------------------------------------------------------------------------------------------------ PNG encoder

    private static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter: none
            raw.Write(rgba, y * width * 4, width * 4);
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw.GetBuffer(), 0, (int)raw.Length);

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var header = new byte[13];
        WriteBigEndian(header, 0, width); WriteBigEndian(header, 4, height);
        header[8] = 8; header[9] = 6; // 8 bits per channel, RGBA
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        s.Write(length);
        var body = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        s.Write(body);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, unchecked((int)Crc32(body)));
        s.Write(crc);
    }

    private static void WriteBigEndian(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
