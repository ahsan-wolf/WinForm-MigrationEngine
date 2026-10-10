using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>Raw source of one Form/UserControl pair.</summary>
public sealed record WinFormsSourceText(string DesignerSource, string CodeBehindSource);

internal static class CodeBehind
{
    private const string WinFormsNamespace = "System.Windows.Forms.";

    /// <summary>The class matching the designer's class name (a code-behind file may hold several), else the first class.</summary>
    public static ClassDeclarationSyntax? FindClass(string source, string? className)
    {
        var classes = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
        return classes.FirstOrDefault(c => c.Identifier.Text == className) ?? classes.FirstOrDefault();
    }

    /// <summary>Qualifies the two well-known base types so RootControl.TagName is always the full .NET type.</summary>
    public static string NormalizeBaseType(string baseType) =>
        baseType is "Form" or "UserControl" ? WinFormsNamespace + baseType : baseType;
}
