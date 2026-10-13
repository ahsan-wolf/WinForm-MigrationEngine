using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Engine.Contracts;
using Migrator.Source.WinForms.Extraction;

namespace Migrator.Source.WinForms;

/// <summary>
/// Finds Designer/code-behind pairs under a root folder, classifies each as Form or UserControl by following the
/// base-type chain across the project, and detects the startup form and target framework. Syntax only.
/// </summary>
public sealed partial class WinFormsProjectAnalyzer : IProjectAnalyzer
{
    private static readonly string[] SkippedDirectories = { "bin", "obj", ".git", ".vs", "node_modules" };

    public ProjectAnalysis Analyze(string rootPath, AnalysisOptions? options = null)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Source folder not found: {rootPath}");

        var extraRoots = (options ?? new AnalysisOptions()).IncludeReferencedProjects ? FindReferencedProjectRoots(rootPath) : new List<string>();

        var pairs = EnumerateFiles(rootPath, "*.Designer.cs")
            .Select(d => (Designer: d, CodeBehind: d[..^".Designer.cs".Length] + ".cs"))
            .Where(p => File.Exists(p.CodeBehind) && File.ReadAllText(p.Designer).Contains("InitializeComponent"))
            .ToList();

        // class name -> declared base type, from EVERY .cs file, so chains through helper bases without a designer
        // (BaseForm, BaseUserControl) still resolve.
        var baseOf = new Dictionary<string, string>();
        var filesOf = new Dictionary<string, List<string>>();
        foreach (var file in new[] { rootPath }.Concat(extraRoots).SelectMany(r => EnumerateFiles(r, "*.cs")))
            foreach (var cls in CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot()
                         .DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (cls.BaseList?.Types.FirstOrDefault()?.Type.ToString() is { } baseType)
                    baseOf.TryAdd(cls.Identifier.ValueText, baseType);
                if (!filesOf.TryGetValue(cls.Identifier.ValueText, out var list)) filesOf[cls.Identifier.ValueText] = list = new List<string>();
                if (!list.Contains(file)) list.Add(file);
            }

        var named = new List<(string Name, string Designer, string CodeBehind)>();
        foreach (var (designer, codeBehind) in pairs)
        {
            var className = WinFormsDesignerParser.Parse(File.ReadAllText(designer)).ClassName;
            var cls = CodeBehind.FindClass(File.ReadAllText(codeBehind), className);
            if (cls != null) named.Add((cls.Identifier.Text, designer, codeBehind));
        }

        var forms = new List<SourceUnit>();
        var userControls = new List<SourceUnit>();
        var resolver = new WinFormsTypeResolver(baseOf);
        foreach (var (name, designer, codeBehind) in named.OrderBy(n => n.Name, StringComparer.Ordinal))
        {
            // A reusable control: a user control, or a custom control derived from any other standard control (`X : ComboBox` with a designer).
            var kind = IsUserControl(name, baseOf) || resolver.IsControlLike(name) ? SourceUnitKind.UserControl : SourceUnitKind.Form;
            (kind == SourceUnitKind.UserControl ? userControls : forms).Add(new SourceUnit(name, kind, designer, codeBehind));
        }

        return new ProjectAnalysis(rootPath, FindFrameworkVersion(rootPath), FindStartupForm(rootPath), forms, userControls, baseOf, filesOf.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value), extraRoots);
    }

    private static bool IsUserControl(string className, Dictionary<string, string> baseOf)
    {
        var seen = new HashSet<string>();
        for (var current = className; baseOf.TryGetValue(current, out var baseType) && seen.Add(current);)
        {
            var simple = baseType[(baseType.LastIndexOf('.') + 1)..];
            if (simple == "UserControl") return true;
            current = simple;
        }
        return false;
    }

    private static string? FindStartupForm(string root)
    {
        foreach (var program in EnumerateFiles(root, "Program.cs"))
        {
            var match = StartupRegex().Match(File.ReadAllText(program));
            if (match.Success) return match.Groups[1].Value[(match.Groups[1].Value.LastIndexOf('.') + 1)..];
        }
        return null;
    }

    private static string? FindFrameworkVersion(string root)
    {
        var csproj = EnumerateFiles(root, "*.csproj").FirstOrDefault();
        if (csproj == null) return null;
        try
        {
            var props = XDocument.Load(csproj).Descendants()
                .FirstOrDefault(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks" or "TargetFrameworkVersion");
            return props?.Value.Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Folders of projects referenced by `ProjectReference` that lie outside the analysed folder (generated entity layers, shared libraries), followed transitively.
    /// A referenced project whose folder contains the analysed folder is skipped (it would pull in everything). Reading project files only.
    /// </summary>
    private static List<string> FindReferencedProjectRoots(string rootPath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        var queue = new Queue<string>(EnumerateFiles(rootPath, "*.csproj"));

        while (queue.Count > 0 && roots.Count < 40)
        {
            var project = Path.GetFullPath(queue.Dequeue());
            if (!visited.Add(project)) continue;

            XDocument doc;
            try { doc = XDocument.Load(project); }
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException) { continue; }

            foreach (var include in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference").Select(e => e.Attribute("Include")?.Value).OfType<string>())
            {
                var normalized = include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, normalized));
                if (!File.Exists(target) || target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || visited.Contains(target)) continue;

                var folder = Path.GetDirectoryName(target)!;
                if (root.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue; // contains the analysed folder
                if (!roots.Contains(folder, StringComparer.OrdinalIgnoreCase)) roots.Add(folder);
                queue.Enqueue(target);
            }
        }
        return roots;
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar)[..^1]
                .Any(seg => SkippedDirectories.Contains(seg, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"Application\s*\.\s*Run\s*\(\s*new\s+([\w\.]+)")]
    private static partial Regex StartupRegex();
}
