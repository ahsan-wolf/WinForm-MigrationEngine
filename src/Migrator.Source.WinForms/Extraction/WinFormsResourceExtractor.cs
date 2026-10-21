using System.Text.RegularExpressions;
using Migrator.Engine.Contracts;
using Migrator.Engine.Support;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// The images of the project's <c>Resources.resx</c> that code refers to (<c>Properties.Resources.Name</c> or <c>Resources.Name</c>): embedded byte arrays and
/// file references (<c>ResXFileRef</c>, a path relative to the .resx). Only referenced names are kept, so a resource file with hundreds of images does not bloat the IR.
/// </summary>
internal static partial class WinFormsResourceExtractor
{
    private const int MaxTotalBytes = 4 * 1024 * 1024;

    public static ResourceFileIr Extract(ProjectAnalysis analysis)
    {
        var file = new ResourceFileIr();
        var resxFiles = FindResourceFiles(analysis);
        if (resxFiles.Count == 0) return file;

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in SourceFiles(analysis))
            foreach (Match m in ReferenceRegex().Matches(ReadSafe(source)))
                referenced.Add(m.Groups[1].Value);
        if (referenced.Count == 0) return file;

        var total = 0;
        foreach (var resx in resxFiles)
        {
            var xml = ReadSafe(resx);
            var images = ResxReader.ParseImages(xml);
            foreach (var (name, bytes) in ResxReader.ParseFileReferences(xml, Path.GetDirectoryName(resx)!)) images.TryAdd(name, bytes);
            foreach (var (name, bytes) in images.OrderBy(i => i.Key, StringComparer.Ordinal))
            {
                if (!referenced.Contains(name) || file.Images.Any(i => i.Name == name) || total + bytes.Length > MaxTotalBytes) continue;
                total += bytes.Length;
                file.Images.Add(new ResourceImageIr { Name = name, Format = ResxReader.Sniff(bytes), Data = Convert.ToBase64String(bytes) });
            }
        }
        return file;
    }

    private static List<string> FindResourceFiles(ProjectAnalysis analysis) =>
        new[] { analysis.RootPath }.Concat(analysis.AdditionalRoots ?? Array.Empty<string>())
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "Resources.resx", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static IEnumerable<string> SourceFiles(ProjectAnalysis analysis) =>
        analysis.Forms.Concat(analysis.UserControls).SelectMany(u => new[] { u.CodeBehindPath, u.DesignerPath })
            .Concat((analysis.TypeFiles ?? new Dictionary<string, IReadOnlyList<string>>()).Values.SelectMany(v => v))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string ReadSafe(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return ""; }
    }

    [GeneratedRegex(@"\bResources\s*\.\s*(\w+)\b")]
    private static partial Regex ReferenceRegex();
}
