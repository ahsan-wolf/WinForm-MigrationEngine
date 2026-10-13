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

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _typeFiles;

    /// <param name="typeFiles">Simple class name -> source files declaring it (all projects). Lets members of project base classes be merged into derived units.</param>
    public WinFormsMemberExtractor(IReadOnlyDictionary<string, IReadOnlyList<string>>? typeFiles = null) =>
        _typeFiles = typeFiles ?? new Dictionary<string, IReadOnlyList<string>>();

    public void Extract(IrExtractionContext context, PageIr target)
    {
        var designer = WinFormsDesignerParser.Parse(context.MarkupContent);
        var controlFields = new HashSet<string>(designer.DeclaredControlTypes.Keys);

        var classDecl = CodeBehind.FindClass(context.CodeBehindContent ?? "", designer.ClassName);
        if (classDecl == null) return;

        AddMembers(classDecl, target, controlFields, inheritedFrom: null);
        AddInheritedMembers(classDecl, designer.ClassName, target, controlFields);
    }

    /// <summary>
    /// Code inheritance: a screen derived from a project base class uses the base fields, properties and methods as its own.
    /// They are copied into the derived unit (marked <c>inherited:Base</c>) unless the derived class already declares the member, closest base first.
    /// Constructors, designer boilerplate and the designer partial of the base (its controls) are not merged.
    /// </summary>
    private void AddInheritedMembers(ClassDeclarationSyntax derived, string className, PageIr target, HashSet<string> controlFields)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { className };
        var current = derived.BaseList?.Types.FirstOrDefault()?.Type.ToString();
        while (current is not null)
        {
            var simple = current[(current.LastIndexOf('.') + 1)..];
            var generic = simple.IndexOf('<');
            if (generic >= 0) simple = simple[..generic];
            if (!seen.Add(simple) || !_typeFiles.TryGetValue(simple, out var files)) return;

            string? nextBase = null;
            foreach (var file in files.Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var cls in Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetCompilationUnitRoot(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(file)))
                             .DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.ValueText == simple))
                {
                    AddMembers(cls, target, controlFields, inheritedFrom: simple);
                    nextBase ??= cls.BaseList?.Types.FirstOrDefault()?.Type.ToString();
                }
            }
            current = nextBase;
        }
    }

    private static bool Declared(PageIr target, MemberIr m) =>
        target.Members.Any(x => x.Kind == m.Kind && x.Name == m.Name && x.Parameters.Count == m.Parameters.Count);

    private static void AddMembers(ClassDeclarationSyntax classDecl, PageIr target, HashSet<string> controlFields, string? inheritedFrom)
    {
        void Add(MemberIr m)
        {
            if (inheritedFrom is not null)
            {
                if (Declared(target, m)) return; // the derived class (or a closer base) wins
                m.Attributes.Add("inherited:" + inheritedFrom);
            }
            target.Members.Add(m);
        }

        foreach (var member in classDecl.Members)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax prop:
                    var property = Simple(MemberKind.Property, prop.Identifier.ValueText, prop.Type.ToString(), prop.Modifiers);
                    property.Body = prop.Initializer?.Value.ToString(); // auto-property initializer; for fields/properties Body is the initial value expression
                    Add(property);
                    break;

                case FieldDeclarationSyntax field:
                    foreach (var v in field.Declaration.Variables)
                    {
                        var name = v.Identifier.ValueText;
                        if (controlFields.Contains(name) || name == "components") continue;
                        var fieldMember = Simple(MemberKind.Field, name, field.Declaration.Type.ToString(), field.Modifiers);
                        fieldMember.Body = v.Initializer?.Value.ToString(); // initial value expression (constants such as c_NoFilter = "..." matter to translated code)
                        Add(fieldMember);
                    }
                    break;

                case EventFieldDeclarationSyntax evt:
                    foreach (var v in evt.Declaration.Variables)
                        Add(Simple(MemberKind.Event, v.Identifier.ValueText, evt.Declaration.Type.ToString(), evt.Modifiers));
                    break;

                case ConstructorDeclarationSyntax ctor when inheritedFrom is null && !ctor.Modifiers.Any(m => m.Text == "static"):
                    // Recorded as a method named ".ctor": its parameters become the page inputs (props) in generators.
                    var ctorIr = Simple(MemberKind.Method, ".ctor", "void", ctor.Modifiers);
                    ctorIr.Parameters = ctor.ParameterList.Parameters.Select(p => new ParameterIr
                    {
                        Name = p.Identifier.ValueText,
                        Type = p.Type?.ToString() ?? "object",
                        DefaultValue = p.Default?.Value.ToString()
                    }).ToList();
                    ctorIr.Body = ctor.Body?.ToString() ?? ctor.ExpressionBody?.ToString();
                    target.Members.Add(ctorIr);
                    break;

                case MethodDeclarationSyntax method when !DesignerBoilerplateMethods.Contains(method.Identifier.ValueText):
                    var ir = Simple(MemberKind.Method, method.Identifier.ValueText, method.ReturnType.ToString(), method.Modifiers);
                    ir.Parameters = method.ParameterList.Parameters.Select(p => new ParameterIr
                    {
                        Name = p.Identifier.ValueText,
                        Type = p.Type?.ToString() ?? "object",
                        DefaultValue = p.Default?.Value.ToString()
                    }).ToList();
                    ir.Body = method.Body?.ToString() ?? method.ExpressionBody?.ToString();
                    ir.Attributes = method.AttributeLists.SelectMany(l => l.Attributes).Select(a => a.Name.ToString()).ToList();
                    Add(ir);
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
