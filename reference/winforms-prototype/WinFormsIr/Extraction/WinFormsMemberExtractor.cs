using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Models;

namespace WebFormsMigrator.WinFormsAnalyzers
{
    /// <summary>
    /// Mirrors RoslynMemberExtractor (PROJECT_DOCUMENTATION.md §2.3) but reads the
    /// WinForms code-behind file (not Designer.cs) and skips members that are just
    /// UI control backing fields, since those are already represented as
    /// ControlNodeIr nodes rather than business-logic members.
    /// </summary>
    public static class WinFormsMemberExtractor
    {
        private static readonly HashSet<string> DesignerBoilerplateMethods = new() { "InitializeComponent", "Dispose" };

        public static List<MemberIr> Extract(string codeBehindSourceText, HashSet<string> controlFieldNames)
        {
            var members = new List<MemberIr>();
            var tree = CSharpSyntaxTree.ParseText(codeBehindSourceText);
            var root = tree.GetCompilationUnitRoot();
            var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDecl == null) return members;

            foreach (var member in classDecl.Members)
            {
                if (member is PropertyDeclarationSyntax prop)
                {
                    members.Add(new MemberIr
                    {
                        Kind = (int)MemberKind.Property,
                        Name = prop.Identifier.Text,
                        Type = prop.Type.ToString(),
                        Visibility = GetVisibility(prop.Modifiers),
                        IsStatic = HasModifier(prop.Modifiers, "static"),
                        Body = null
                    });
                }
                else if (member is FieldDeclarationSyntax field)
                {
                    foreach (var v in field.Declaration.Variables)
                    {
                        if (controlFieldNames.Contains(v.Identifier.Text)) continue; // it's a control, not business state
                        if (v.Identifier.Text == "components") continue;

                        members.Add(new MemberIr
                        {
                            Kind = (int)MemberKind.Field,
                            Name = v.Identifier.Text,
                            Type = field.Declaration.Type.ToString(),
                            Visibility = GetVisibility(field.Modifiers),
                            IsStatic = HasModifier(field.Modifiers, "static"),
                            Body = null
                        });
                    }
                }
                else if (member is EventFieldDeclarationSyntax evt)
                {
                    foreach (var v in evt.Declaration.Variables)
                    {
                        members.Add(new MemberIr
                        {
                            Kind = (int)MemberKind.Event,
                            Name = v.Identifier.Text,
                            Type = evt.Declaration.Type.ToString(),
                            Visibility = GetVisibility(evt.Modifiers),
                            IsStatic = HasModifier(evt.Modifiers, "static"),
                            Body = null
                        });
                    }
                }
                else if (member is MethodDeclarationSyntax method)
                {
                    if (DesignerBoilerplateMethods.Contains(method.Identifier.Text)) continue;

                    members.Add(new MemberIr
                    {
                        Kind = (int)MemberKind.Method,
                        Name = method.Identifier.Text,
                        Type = method.ReturnType.ToString(),
                        Visibility = GetVisibility(method.Modifiers),
                        IsStatic = HasModifier(method.Modifiers, "static"),
                        Parameters = method.ParameterList.Parameters.Select(p => new ParameterIr
                        {
                            Name = p.Identifier.Text,
                            Type = p.Type?.ToString() ?? "object",
                            DefaultValue = p.Default?.Value.ToString()
                        }).ToList(),
                        Body = method.Body?.ToString() ?? method.ExpressionBody?.ToString()
                    });
                }
            }

            return members;
        }

        private static bool HasModifier(SyntaxTokenList modifiers, string text) =>
            modifiers.Any(m => m.Text == text);

        private static int GetVisibility(SyntaxTokenList modifiers)
        {
            if (HasModifier(modifiers, "public")) return (int)MemberVisibility.Public;
            return (int)MemberVisibility.Private; // private / protected / internal / implicit-default
        }
    }
}
