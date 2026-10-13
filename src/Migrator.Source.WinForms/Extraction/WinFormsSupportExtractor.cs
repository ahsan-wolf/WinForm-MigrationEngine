using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Engine.Contracts;
using Migrator.Engine.Support;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Extracts the project's own non-UI types (static helper modules such as `main`, settings/data classes, enums, structs) from every
/// <c>.cs</c> file. Forms and user controls are skipped (they are units); so are designer/resource files and build output.
/// </summary>
internal static class WinFormsSupportExtractor
{
    private static readonly string[] SkippedFolders = { "bin", "obj", "Properties", ".git", ".vs", "packages" };

    public static SupportFileIr Extract(ProjectAnalysis analysis)
    {
        var unitNames = analysis.Forms.Concat(analysis.UserControls).Select(u => u.Name).ToHashSet(StringComparer.Ordinal);
        var resolver = new WinFormsTypeResolver(analysis.TypeBases);
        var file = new SupportFileIr();

        foreach (var path in Directory.EnumerateFiles(analysis.RootPath, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(analysis.RootPath, path).Replace('\\', '/');
            if (relative.Split('/').SkipLast(1).Any(part => SkippedFolders.Contains(part, StringComparer.OrdinalIgnoreCase))) continue;
            if (path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("Program.cs", StringComparison.OrdinalIgnoreCase)) continue;

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
            foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                // Controls and components (including custom ones derived from them) are UI, not support code.
                if (unitNames.Contains(type.Identifier.ValueText) || IsUiType(type) || type is not EnumDeclarationSyntax && resolver.Resolve(type.Identifier.ValueText) is not null) continue;
                var container = (type.Parent as BaseTypeDeclarationSyntax)?.Identifier.ValueText;
                file.Classes.Add(Build(type, relative, container));
            }
        }

        // The same simple name in two namespaces would collide in the generated modules; the first wins and the rest are reported.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var duplicate in file.Classes.Where(c => !seen.Add(c.Name)).ToList())
        {
            file.Classes.Remove(duplicate);
            file.Classes.First(c => c.Name == duplicate.Name).Unsupported.Add($"duplicate type name in {duplicate.RelativePath} was not extracted");
        }
        return file;
    }

    /// <summary>Custom controls, forms and dialogs not found as units (base type Form/UserControl/Control/...): UI, not support code.</summary>
    private static bool IsUiType(BaseTypeDeclarationSyntax type) =>
        type.BaseList?.Types.Any(t => t.Type.ToString() is "Form" or "UserControl" or "Control" or "System.Windows.Forms.Form" or "System.Windows.Forms.UserControl"
                                      || t.Type.ToString().EndsWith("Form", StringComparison.Ordinal)) == true;

    private static SupportClassIr Build(BaseTypeDeclarationSyntax type, string relativePath, string? container)
    {
        var ir = new SupportClassIr
        {
            Name = type.Identifier.ValueText,
            Kind = type is EnumDeclarationSyntax ? "enum" : type is StructDeclarationSyntax ? "struct" : "class",
            IsStatic = type.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)),
            Container = container,
            BaseType = type.BaseList?.Types.FirstOrDefault()?.Type.ToString(),
            RelativePath = relativePath
        };

        if (type is EnumDeclarationSyntax enumDecl)
        {
            ir.EnumValues = enumDecl.Members.Select(m => new SupportEnumValueIr { Name = m.Identifier.ValueText, Value = m.EqualsValue?.Value.ToString() }).ToList();
            return ir;
        }

        foreach (var member in ((TypeDeclarationSyntax)type).Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (var v in field.Declaration.Variables)
                    {
                        var m = Simple(MemberKind.Field, v.Identifier.ValueText, field.Declaration.Type.ToString(), field.Modifiers);
                        m.Body = v.Initializer?.Value.ToString();
                        AddModifiers(m, field.Modifiers);
                        ir.Members.Add(m);
                    }
                    break;

                case PropertyDeclarationSyntax prop:
                    ir.Members.Add(Property(prop));
                    break;

                case MethodDeclarationSyntax method:
                    var mm = Simple(MemberKind.Method, method.Identifier.ValueText, method.ReturnType.ToString(), method.Modifiers);
                    mm.Parameters = Parameters(method.ParameterList);
                    mm.Body = method.Body?.ToString() ?? (method.ExpressionBody is { } eb ? "{ return " + eb.Expression + "; }" : null);
                    AddModifiers(mm, method.Modifiers);
                    ir.Members.Add(mm);
                    break;

                case ConstructorDeclarationSyntax ctor when !ctor.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)):
                    var cm = Simple(MemberKind.Method, ".ctor", "void", ctor.Modifiers);
                    cm.Parameters = Parameters(ctor.ParameterList);
                    cm.Body = ctor.Body?.ToString();
                    ir.Members.Add(cm);
                    break;

                case BaseTypeDeclarationSyntax: // nested types are extracted on their own
                    break;

                default:
                    ir.Unsupported.Add($"{member.Kind()}: {Collapse(member)}");
                    break;
            }
        }
        return ir;
    }

    private static MemberIr Property(PropertyDeclarationSyntax prop)
    {
        var m = Simple(MemberKind.Property, prop.Identifier.ValueText, prop.Type.ToString(), prop.Modifiers);
        AddModifiers(m, prop.Modifiers);
        if (prop.ExpressionBody is { } expr)
        {
            m.Body = "{ get { return " + expr.Expression + "; } }";
            m.Attributes.Add("mod:accessors");
        }
        else if (prop.AccessorList is { } accessors && accessors.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
        {
            var parts = accessors.Accessors.Select(a => a.Keyword.Text + " " + (a.Body?.ToString()
                ?? (a.ExpressionBody is { } e ? "{ " + (a.IsKind(SyntaxKind.GetAccessorDeclaration) ? "return " : "") + e.Expression + "; }" : ";")));
            m.Body = "{ " + string.Join(" ", parts) + " }";
            m.Attributes.Add("mod:accessors");
        }
        else
        {
            m.Body = prop.Initializer?.Value.ToString();
            m.Attributes.Add("mod:auto");
            if (prop.AccessorList?.Accessors.All(a => !a.Keyword.IsKind(SyntaxKind.SetKeyword)) == true) m.Attributes.Add("mod:readonly");
        }
        return m;
    }

    private static List<ParameterIr> Parameters(ParameterListSyntax list) => list.Parameters.Select(p => new ParameterIr
    {
        Name = p.Identifier.ValueText,
        Type = (p.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword)) ? p.Modifiers.First().Text + " " : "") + (p.Type?.ToString() ?? "object"),
        DefaultValue = p.Default?.Value.ToString()
    }).ToList();

    private static void AddModifiers(MemberIr m, SyntaxTokenList modifiers)
    {
        foreach (var (kind, tag) in new[] { (SyntaxKind.ConstKeyword, "mod:const"), (SyntaxKind.ReadOnlyKeyword, "mod:readonly"), (SyntaxKind.AsyncKeyword, "mod:async") })
            if (modifiers.Any(x => x.IsKind(kind))) m.Attributes.Add(tag);
    }

    private static MemberIr Simple(MemberKind kind, string name, string type, SyntaxTokenList modifiers) => new()
    {
        Kind = kind,
        Name = name,
        Type = type,
        Visibility = modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)) ? Visibility.Public
            : modifiers.Any(x => x.IsKind(SyntaxKind.ProtectedKeyword)) ? Visibility.Protected
            : modifiers.Any(x => x.IsKind(SyntaxKind.InternalKeyword)) ? Visibility.Internal : Visibility.Private,
        IsStatic = modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)) || modifiers.Any(x => x.IsKind(SyntaxKind.ConstKeyword))
    };

    private static string Collapse(SyntaxNode n)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(n.ToString(), @"\s+", " ").Trim();
        return text.Length > 120 ? text[..120] + "..." : text;
    }
}
