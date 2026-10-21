using System.Text.RegularExpressions;
using Migrator.Engine.Support;

namespace Migrator.Target.React.Translation;

/// <summary>
/// Images by name for `Properties.Resources.Name` and for image-list entries the IR could not carry: first the project's resource file as extracted into
/// resources.ir.json, then an optional folder given with `--images` (files named by key: `Name.png`, `Name.ico`, ...).
/// </summary>
public sealed partial class ResourceImages
{
    public static ResourceImages Empty { get; } = new(null, null);

    private static readonly string[] Extensions = { "png", "gif", "jpg", "jpeg", "bmp", "ico", "svg" };

    private readonly Dictionary<string, (byte[] Data, string Format)> _embedded = new(StringComparer.Ordinal);
    private readonly string? _folder;

    public ResourceImages(ResourceFileIr? file, string? imagesFolder)
    {
        foreach (var image in file?.Images ?? new List<ResourceImageIr>())
            try { _embedded[image.Name] = (Convert.FromBase64String(image.Data), image.Format); }
            catch (FormatException) { /* not base64: the image is unusable */ }
        _folder = !string.IsNullOrWhiteSpace(imagesFolder) && Directory.Exists(imagesFolder) ? imagesFolder : null;
    }

    public bool TryGet(string name, out byte[] data, out string format)
    {
        if (_embedded.TryGetValue(name, out var found)) { (data, format) = found; return true; }
        if (_folder is not null && name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            foreach (var ext in Extensions)
            {
                var path = Path.Combine(_folder, $"{name}.{ext}");
                if (!File.Exists(path)) continue;
                data = File.ReadAllBytes(path);
                format = ext == "jpeg" ? "jpg" : ext;
                return true;
            }
        }
        data = Array.Empty<byte>();
        format = "";
        return false;
    }

    /// <summary>The web path the image is written to (`images/resources/Name.png`; bmp and ico become png), or null when there is no such image.</summary>
    public string? WebPath(string name) =>
        TryGet(name, out _, out var format)
            ? $"images/resources/{UnsafeChars().Replace(name, "_")}.{(format is "bmp" or "ico" ? "png" : format)}"
            : null;

    [GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeChars();
}
