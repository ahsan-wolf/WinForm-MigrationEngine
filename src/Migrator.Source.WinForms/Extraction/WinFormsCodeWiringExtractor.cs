using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Facet "codeWiring": event subscriptions written in the code-behind (constructor, Load, helper methods) instead of the designer,
/// <c>btn.Click += btn_Click</c>, <c>this.Load += new EventHandler(OnLoad)</c> or <c>btn.Click += (s, e) => ...</c>.
/// They are recorded exactly like designer wiring (<c>Attributes["OnClick"] = handler</c>), plus the marker <c>Wired.Click = "code"</c>, so workflow,
/// navigation and every generator treat them as ordinary wiring. A lambda becomes a synthetic handler method (<c>btn_Click_lambda1</c>) whose body is the
/// lambda body. Subscriptions on controls that are not in the tree (created at run time), a second handler for an event that is already wired,
/// and handlers that are not methods of this page are left alone.
/// </summary>
public sealed class WinFormsCodeWiringExtractor : IIrExtractor
{
    public const string WiredMarkerPrefix = "Wired.";

    public string Facet => "codeWiring";

    public void Extract(IrExtractionContext context, PageIr target)
    {
        if (target.RootControl is null) return;
        var designer = WinFormsDesignerParser.Parse(context.MarkupContent);
        var classDecl = CodeBehind.FindClass(context.CodeBehindContent ?? "", designer.ClassName);
        if (classDecl is null) return;

        var controls = new Dictionary<string, ControlNodeIr>(StringComparer.Ordinal);
        void Walk(ControlNodeIr n) { controls.TryAdd(n.Id, n); foreach (var c in n.Children) Walk(c); }
        Walk(target.RootControl);

        var methodNames = new HashSet<string>(target.Members.Where(m => m.Kind == MemberKind.Method).Select(m => m.Name), StringComparer.Ordinal);
        var lambdaCount = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var method in classDecl.Members.OfType<BaseMethodDeclarationSyntax>().Where(m => m is MethodDeclarationSyntax or ConstructorDeclarationSyntax))
        {
            if (method is MethodDeclarationSyntax { Identifier.ValueText: "InitializeComponent" }) continue;
            foreach (var a in method.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(a => a.IsKind(SyntaxKind.AddAssignmentExpression)))
            {
                if (a.Left is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var evt } left) continue;
                var node = ControlOf(left.Expression, target.RootControl, controls);
                if (node is null) continue;

                var attribute = "On" + evt;
                if (node.Attributes.ContainsKey(attribute)) continue; // designer wiring (or an earlier subscription) wins; one handler per event in the IR

                var handler = HandlerName(a.Right, methodNames);
                if (handler is null && a.Right is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                    handler = AddLambda(a.Right, node.Id, evt, lambdaCount, target, methodNames);
                if (handler is null) continue;

                node.Attributes[attribute] = handler;
                node.Attributes[WiredMarkerPrefix + evt] = "code";
            }
        }
    }

    /// <summary>The control a subscription is on: `ctl`, `this.ctl` or the form itself (`this`).</summary>
    private static ControlNodeIr? ControlOf(ExpressionSyntax e, ControlNodeIr root, Dictionary<string, ControlNodeIr> controls) => e switch
    {
        ThisExpressionSyntax => root,
        IdentifierNameSyntax id when controls.TryGetValue(id.Identifier.ValueText, out var node) => node,
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: var n } when controls.TryGetValue(n.Identifier.ValueText, out var node) => node,
        _ => null
    };

    /// <summary>`handler`, `this.handler` or `new SomeEventHandler(handler)` naming a method of this page.</summary>
    private static string? HandlerName(ExpressionSyntax e, HashSet<string> methods)
    {
        var name = e switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: var n } => n.Identifier.ValueText,
            ObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } args } => HandlerName(args[0].Expression, methods),
            _ => null
        };
        return name is not null && methods.Contains(name) ? name : null;
    }

    private static string AddLambda(ExpressionSyntax lambda, string controlId, string evt, Dictionary<string, int> counts, PageIr target, HashSet<string> methodNames)
    {
        var key = $"{controlId}_{evt}";
        counts[key] = counts.GetValueOrDefault(key) + 1;
        var name = $"{key}_lambda{counts[key]}";

        var (parameters, body) = lambda switch
        {
            ParenthesizedLambdaExpressionSyntax p => (p.ParameterList.Parameters, (CSharpSyntaxNode)p.Body),
            SimpleLambdaExpressionSyntax s => (SyntaxFactory.SeparatedList(new[] { s.Parameter }), s.Body),
            AnonymousMethodExpressionSyntax m => (m.ParameterList?.Parameters ?? default, m.Body),
            _ => (default, (CSharpSyntaxNode)lambda)
        };

        var text = body is BlockSyntax block ? block.ToString() : "{ " + body.ToString() + "; }";
        var ir = new MemberIr { Kind = MemberKind.Method, Name = name, Type = "void", Visibility = Visibility.Private, Body = text, Attributes = new List<string> { "synthetic:lambda" } };
        var names = new[] { "sender", "e" };
        var types = new[] { "object", "EventArgs" };
        var list = parameters.ToList();
        for (var i = 0; i < Math.Max(2, list.Count); i++)
            ir.Parameters.Add(new ParameterIr { Name = i < list.Count ? list[i].Identifier.ValueText : names[Math.Min(i, 1)], Type = i < list.Count && list[i].Type is { } t ? t.ToString() : types[Math.Min(i, 1)] });
        // a lambda written without parameters (`delegate { ... }`) still gets the usual (sender, e) shape so generators see an event handler
        if (list.Count == 0) { ir.Parameters.Clear(); ir.Parameters.Add(new ParameterIr { Name = "sender", Type = "object" }); ir.Parameters.Add(new ParameterIr { Name = "e", Type = "EventArgs" }); }
        target.Members.Add(ir);
        methodNames.Add(name);
        return name;
    }
}
