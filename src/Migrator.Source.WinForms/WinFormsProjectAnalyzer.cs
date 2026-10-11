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

    public ProjectAnalysis Analyze(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Source folder not found: {rootPath}");

        var pairs = EnumerateFiles(rootPath, "*.Designer.cs")
            .Select(d => (Designer: d, CodeBehind: d[..^".Designer.cs".Length] + ".cs"))
            .Where(p => File.Exists(p.CodeBehind) && File.ReadAllText(p.Designer).Contains("InitializeComponent"))
            .ToList();

        // class name -> declared base type, from EVERY .cs file, so chains through helper bases without a designer
        // (BaseForm, BaseUserControl) still resolve.
        var baseOf = new Dictionary<string, string>();
        foreach (var file in EnumerateFiles(rootPath, "*.cs"))
            foreach (var cls in CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot()
                         .DescendantNodes().OfType<ClassDeclarationSyntax>())
                if (cls.BaseList?.Types.FirstOrDefault()?.Type.ToString() is { } baseType)
                    baseOf.TryAdd(cls.Identifier.Text, baseType);

        var named = new List<(string Name, string Designer, string CodeBehind)>();
        foreach (var (designer, codeBehind) in pairs)
        {
            var className = WinFormsDesignerParser.Parse(File.ReadAllText(designer)).ClassName;
            var cls = CodeBehind.FindClass(File.ReadAllText(codeBehind), className);
            if (cls != null) named.Add((cls.Identifier.Text, designer, codeBehind));
        }

        var forms = new List<SourceUnit>();
        var userControls = new List<SourceUnit>();
        foreach (var (name, designer, codeBehind) in named.OrderBy(n => n.Name, StringComparer.Ordinal))
        {
            var kind = IsUserControl(name, baseOf) ? SourceUnitKind.UserControl : SourceUnitKind.Form;
            (kind == SourceUnitKind.UserControl ? userControls : forms).Add(new SourceUnit(name, kind, designer, codeBehind));
        }

        return new ProjectAnalysis(rootPath, FindFrameworkVersion(rootPath), FindStartupForm(rootPath), forms, userControls);
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

    private static IEnumerable<string> EnumerateFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar)[..^1]
                .Any(seg => SkippedDirectories.Contains(seg, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"Application\s*\.\s*Run\s*\(\s*new\s+([\w\.]+)")]
    private static partial Regex StartupRegex();
}
