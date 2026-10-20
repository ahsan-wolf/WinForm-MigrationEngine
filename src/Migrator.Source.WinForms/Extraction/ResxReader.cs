using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>Reads the plain-string entries of a Form/UserControl .resx (e.g. "btnSave.Text" -> "Save").</summary>
internal static partial class ResxReader
{
    /// <summary>Largest image kept per resource (bytes); bigger ones are left out and the control gets a placeholder icon.</summary>
    public const int MaxImageBytes = 256 * 1024;

    /// <summary>Largest ImageList stream decoded (bytes, still compressed).</summary>
    public const int MaxImageStreamBytes = 2 * 1024 * 1024;

    /// <summary>Only string-valued properties that carry user-visible text are merged; geometry and binary resources are not.</summary>
    public static readonly string[] MergedProperties = { "Text", "ToolTip", "ToolTipText", "HeaderText", "PlaceholderText" };

    public static Dictionary<string, string> Parse(string? resxXml)
    {
        var entries = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(resxXml)) return entries;

        try
        {
            foreach (var data in XDocument.Parse(resxXml).Root?.Elements("data") ?? Enumerable.Empty<XElement>())
            {
                var name = (string?)data.Attribute("name");
                // Skip designer metadata (">>btn.Name") and typed/binary entries (images, icons).
                if (string.IsNullOrEmpty(name) || name.StartsWith(">>") || data.Attribute("type") != null || data.Attribute("mimetype") != null)
                    continue;
                entries[name] = (string?)data.Element("value") ?? "";
            }
        }
        catch (System.Xml.XmlException)
        {
            // A malformed .resx must not fail the form: values simply are not merged.
        }
        return entries;
    }

    /// <summary>
    /// The byte-array entries of a .resx (images and icons stored as base64 file bytes): name -> bytes. Entries that are not image-like, or too large, are skipped;
    /// ImageList streams and other BinaryFormatter blobs are never decoded.
    /// </summary>
    public static Dictionary<string, byte[]> ParseImages(string? resxXml)
    {
        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(resxXml)) return images;
        try
        {
            foreach (var data in XDocument.Parse(resxXml).Root?.Elements("data") ?? Enumerable.Empty<XElement>())
            {
                var name = (string?)data.Attribute("name");
                var type = (string?)data.Attribute("type") ?? "";
                var mime = (string?)data.Attribute("mimetype") ?? "";
                if (string.IsNullOrEmpty(name) || name.StartsWith(">>", StringComparison.Ordinal)) continue;
                if (!mime.Contains("bytearray.base64", StringComparison.Ordinal)) continue;
                if (!(type.StartsWith("System.Drawing.Bitmap", StringComparison.Ordinal) || type.StartsWith("System.Drawing.Icon", StringComparison.Ordinal) || type.StartsWith("System.Drawing.Image", StringComparison.Ordinal))) continue;
                try
                {
                    var bytes = Convert.FromBase64String(((string?)data.Element("value") ?? "").Trim());
                    if (bytes.Length > 0 && bytes.Length <= MaxImageBytes && Sniff(bytes) != "unknown") images[name] = bytes;
                }
                catch (FormatException)
                {
                    // not base64: not an image we can read
                }
            }
        }
        catch (System.Xml.XmlException)
        {
            // A malformed .resx must not fail the form.
        }
        return images;
    }

    /// <summary>The BinaryFormatter ImageList streams of a .resx (`imageList1.ImageStream`): name -> the serialized bytes. Decoded by <see cref="ImageListDecoder"/>.</summary>
    public static Dictionary<string, byte[]> ParseImageStreams(string? resxXml)
    {
        var streams = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(resxXml) || !resxXml.Contains("ImageStream", StringComparison.Ordinal)) return streams;
        try
        {
            foreach (var data in XDocument.Parse(resxXml).Root?.Elements("data") ?? Enumerable.Empty<XElement>())
            {
                var name = (string?)data.Attribute("name");
                if (name is null || !name.EndsWith(".ImageStream", StringComparison.Ordinal) || ((string?)data.Attribute("mimetype") ?? "").Contains("binary.base64", StringComparison.Ordinal) is false) continue;
                try
                {
                    var bytes = Convert.FromBase64String(((string?)data.Element("value") ?? "").Trim());
                    if (bytes.Length > 0 && bytes.Length <= MaxImageStreamBytes) streams[name] = bytes;
                }
                catch (FormatException)
                {
                    // not base64: nothing to decode
                }
            }
        }
        catch (System.Xml.XmlException)
        {
            // A malformed .resx must not fail the form.
        }
        return streams;
    }

    /// <summary>The file format of image bytes from their signature: png, gif, jpg, bmp, ico, svg or unknown.</summary>
    public static string Sniff(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        if (b.Length >= 4 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return "gif";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 2 && b[0] == 'B' && b[1] == 'M') return "bmp";
        if (b.Length >= 6 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) return "ico";
        var head = System.Text.Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 200)).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || (head.StartsWith("<?xml", StringComparison.Ordinal) && head.Contains("<svg", StringComparison.OrdinalIgnoreCase)) ? "svg" : "unknown";
    }

    /// <summary>The resource a control's Image/Icon property points at: `resources.GetObject("name")` in the designer, else `{key}.{property}` of a localizable form.</summary>
    public static string? ImageResourceName(Dictionary<string, string> attributes, string? resourceKey, string property)
    {
        if (attributes.TryGetValue(property, out var expression) && GetObjectRegex().Match(expression) is { Success: true } m) return m.Groups[1].Value;
        return resourceKey is null ? null : $"{resourceKey}.{property}";
    }

    [GeneratedRegex("GetObject\\(\\s*\"([^\"]+)\"\\s*\\)")]
    private static partial Regex GetObjectRegex();

    public static string? ReadSibling(string codeBehindPath)
    {
        var path = Path.ChangeExtension(codeBehindPath, ".resx");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
