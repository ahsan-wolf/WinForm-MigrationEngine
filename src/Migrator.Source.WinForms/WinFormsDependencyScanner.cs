using System.Xml.Linq;
using Migrator.Engine.Contracts;
using Migrator.Engine.Projections;

namespace Migrator.Source.WinForms;

/// <summary>
/// Lists the binary assemblies referenced by the project files under the analysed folder: classic <c>&lt;Reference&gt;</c> items with a HintPath (assemblies
/// that ship with the framework have none and are skipped). Project references are source and are analysed with the rest; package references are listed
/// by name only when their library file can be found next to a classic packages folder. Reads project files as XML; nothing is loaded or executed.
/// </summary>
internal static class WinFormsDependencyScanner
{
    private static readonly string[] SkippedDirectories = { "bin", "obj", ".git", ".vs", "node_modules" };

    public static IReadOnlyList<ExternalAssemblyIr> Find(ProjectAnalysis analysis)
    {
        var found = new Dictionary<string, ExternalAssemblyIr>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in new[] { analysis.RootPath }.Concat(analysis.AdditionalRoots ?? Array.Empty<string>()).SelectMany(r => Directory.EnumerateFiles(r, "*.csproj", SearchOption.AllDirectories))
                     .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => SkippedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase)))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            XDocument doc;
            try { doc = XDocument.Load(project); }
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException) { continue; }

            var projectName = Path.GetFileNameWithoutExtension(project);
            foreach (var reference in doc.Descendants().Where(e => e.Name.LocalName == "Reference"))
            {
                var hint = reference.Elements().FirstOrDefault(e => e.Name.LocalName == "HintPath")?.Value.Trim();
                if (string.IsNullOrEmpty(hint)) continue; // framework / GAC assembly

                var name = (reference.Attribute("Include")?.Value ?? Path.GetFileNameWithoutExtension(hint)).Split(',')[0].Trim();
                var full = ResolvePath(project, hint);
                if (!found.TryGetValue(name, out var item))
                    found[name] = item = new ExternalAssemblyIr { Name = name, Path = full, Exists = File.Exists(full) };
                else if (!item.Exists && File.Exists(full)) { item.Path = full; item.Exists = true; }
                if (!item.ReferencedBy.Contains(projectName)) item.ReferencedBy.Add(projectName);
            }
        }
        return found.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ResolvePath(string projectFile, string hint)
    {
        var normalized = hint.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(Path.GetDirectoryName(projectFile)!, normalized));
    }
}
