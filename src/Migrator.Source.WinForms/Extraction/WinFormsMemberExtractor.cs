using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Facet "members": properties, fields, events and methods of the code-behind class. Skips designer boilerplate and
/// control backing fields (those are already <see cref="ControlNodeIr"/> nodes).
/// </summary>
public sealed class WinFormsMemberExtractor : IIrExtractor
{
    private static readonly HashSet<string> DesignerBoilerplateMethods = new() { "InitializeComponent", "Dispose" };

    public string Facet => "members";

    public void Extract(IrExtractionContext context, PageIr target)
    {
        var designer = WinFormsDesignerParser.Parse(context.MarkupContent);
        var controlFields = new HashSet<string>(designer.DeclaredControlTypes.Keys);

        var classDecl = CodeBehind.FindClass(context.CodeBehindContent ?? "", designer.ClassName);
        if (classDecl == null) return;

        foreach (var member in classDecl.Members)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax prop:
                    target.Members.Add(Simple(MemberKind.Property, prop.Identifier.Text, prop.Type.ToString(), prop.Modifiers));
                    break;

                case FieldDeclarationSyntax field:
                    foreach (var v in field.Declaration.Variables)
                    {
                        var name = v.Identifier.Text;
                        if (controlFields.Contains(name) || name == "components") continue;
                        target.Members.Add(Simple(MemberKind.Field, name, field.Declaration.Type.ToString(), field.Modifiers));
                    }
                    break;

                case EventFieldDeclarationSyntax evt:
                    foreach (var v in evt.Declaration.Variables)
                        target.Members.Add(Simple(MemberKind.Event, v.Identifier.Text, evt.Declaration.Type.ToString(), evt.Modifiers));
                    break;

                case MethodDeclarationSyntax method when !DesignerBoilerplateMethods.Contains(method.Identifier.Text):
                    var ir = Simple(MemberKind.Method, method.Identifier.Text, method.ReturnType.ToString(), method.Modifiers);
                    ir.Parameters = method.ParameterList.Parameters.Select(p => new ParameterIr
                    {
                        Name = p.Identifier.Text,
                        Type = p.Type?.ToString() ?? "object",
                        DefaultValue = p.Default?.Value.ToString()
                    }).ToList();
                    ir.Body = method.Body?.ToString() ?? method.ExpressionBody?.ToString();
                    ir.Attributes = method.AttributeLists.SelectMany(l => l.Attributes).Select(a => a.Name.ToString()).ToList();
                    target.Members.Add(ir);
                    break;
            }
        }
    }

    private static MemberIr Simple(MemberKind kind, string name, string type, SyntaxTokenList modifiers) => new()
    {
        Kind = kind,
        Name = name,
        Type = type,
        Visibility = GetVisibility(modifiers),
        IsStatic = Has(modifiers, "static")
    };

    private static bool Has(SyntaxTokenList modifiers, string text) => modifiers.Any(m => m.Text == text);

    private static Visibility GetVisibility(SyntaxTokenList modifiers) =>
        Has(modifiers, "public") ? Visibility.Public :
        Has(modifiers, "protected") ? Visibility.Protected :
        Has(modifiers, "internal") ? Visibility.Internal :
        Visibility.Private;
}
