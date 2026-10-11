using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Migrator.Source.WinForms.Extraction;

public sealed class DesignerParseResult
{
    /// <summary>Name of the partial class in the Designer.cs file (the Form/UserControl itself).</summary>
    public string ClassName { get; set; } = "";

    /// <summary>Base type as declared on THIS file's class, if any (Designer.cs partials often omit it).</summary>
    public string? BaseTypeName { get; set; }

    /// <summary>Control field name -> declared/instantiated type, e.g. "txtSearch" -> "System.Windows.Forms.TextBox".</summary>
    public Dictionary<string, string> DeclaredControlTypes { get; } = new();

    /// <summary>Control field name -> { PropertyName -> assigned value text }.</summary>
    public Dictionary<string, Dictionary<string, string>> ControlAttributes { get; } = new();

    /// <summary>Control field name -> { EventName -> handler method name }.</summary>
    public Dictionary<string, Dictionary<string, string>> ControlEventHandlers { get; } = new();

    /// <summary>Events wired on the Form/UserControl itself (this.Load += ...) -> handler method name.</summary>
    public Dictionary<string, string> RootEventHandlers { get; } = new();

    /// <summary>Child control id -> parent control id ("" means the parent is the Form/UserControl root).</summary>
    public Dictionary<string, string> ParentOf { get; } = new();

    /// <summary>Controls in first-seen order within InitializeComponent.</summary>
    public List<string> ControlDeclarationOrder { get; } = new();

    /// <summary>Form/UserControl-level property assignments, e.g. this.Text = "Actor Management";</summary>
    public Dictionary<string, string> RootAttributes { get; } = new();
}

/// <summary>Reads InitializeComponent() statement by statement (syntax only; nothing is compiled or run).</summary>
public static class WinFormsDesignerParser
{
    public static DesignerParseResult Parse(string designerSourceText)
    {
        var result = new DesignerParseResult();
        var root = CSharpSyntaxTree.ParseText(designerSourceText).GetCompilationUnitRoot();

        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl == null) return result;

        result.ClassName = classDecl.Identifier.Text;
        result.BaseTypeName = classDecl.BaseList?.Types.FirstOrDefault()?.Type.ToString();

        // 1. Field declarations => backing fields for controls/components.
        foreach (var field in classDecl.Members.OfType<FieldDeclarationSyntax>())
        {
            var typeText = field.Declaration.Type.ToString();
            foreach (var v in field.Declaration.Variables)
            {
                var name = v.Identifier.Text;
                if (name == "components") continue; // IContainer, not a UI control
                result.DeclaredControlTypes[name] = typeText;
                result.ControlAttributes[name] = new Dictionary<string, string>();
                result.ControlEventHandlers[name] = new Dictionary<string, string>();
            }
        }

        // 2. InitializeComponent() body.
        var initMethod = classDecl.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "InitializeComponent");
        if (initMethod?.Body == null) return result;

        foreach (var stmt in initMethod.Body.Statements)
        {
            if (stmt is not ExpressionStatementSyntax exprStmt) continue;

            switch (exprStmt.Expression)
            {
                case AssignmentExpressionSyntax assign when assign.IsKind(SyntaxKind.SimpleAssignmentExpression):
                    HandleAssignment(assign, result);
                    break;
                case AssignmentExpressionSyntax assign when assign.IsKind(SyntaxKind.AddAssignmentExpression):
                    HandleEventWiring(assign, result);
                    break;
                case InvocationExpressionSyntax invoke:
                    HandleControlsAddInvocation(invoke, result);
                    break;
            }
        }

        return result;
    }

    /// <summary>Matches `this.X` and returns X.</summary>
    private static bool TryGetThisMemberName(ExpressionSyntax expr, out string name)
    {
        if (expr is MemberAccessExpressionSyntax ma && ma.Expression is ThisExpressionSyntax)
        {
            name = ma.Name.Identifier.Text;
            return true;
        }
        name = "";
        return false;
    }

    private static void HandleAssignment(AssignmentExpressionSyntax assign, DesignerParseResult result)
    {
        var left = assign.Left;

        // Case A: this.X = new SomeControlType(...);   or   this.Name = "..." (root property)
        // `new` only declares a control when X is a field of the class; this.ClientSize = new Size(..),
        // this.AutoScaleDimensions = new SizeF(..) are properties of the Form/UserControl itself.
        if (TryGetThisMemberName(left, out var directId))
        {
            if (directId == "components") return; // IContainer plumbing, not a control or a property worth keeping

            if (assign.Right is ObjectCreationExpressionSyntax creation && result.DeclaredControlTypes.ContainsKey(directId))
            {
                result.DeclaredControlTypes[directId] = creation.Type.ToString();
                result.ControlAttributes.TryAdd(directId, new Dictionary<string, string>());
                result.ControlEventHandlers.TryAdd(directId, new Dictionary<string, string>());
                if (!result.ControlDeclarationOrder.Contains(directId))
                    result.ControlDeclarationOrder.Add(directId);
            }
            else
            {
                result.RootAttributes[directId] = CleanLiteral(assign.Right.ToString());
            }
            return;
        }

        // Case B: this.X.SomeProperty = value;
        if (left is MemberAccessExpressionSyntax outerMa &&
            outerMa.Expression is MemberAccessExpressionSyntax innerMa &&
            innerMa.Expression is ThisExpressionSyntax)
        {
            var controlId = innerMa.Name.Identifier.Text;
            if (!result.ControlAttributes.TryGetValue(controlId, out var attrs))
            {
                attrs = new Dictionary<string, string>();
                result.ControlAttributes[controlId] = attrs;
            }
            attrs[outerMa.Name.Identifier.Text] = CleanLiteral(assign.Right.ToString());

            if (!result.ControlDeclarationOrder.Contains(controlId))
                result.ControlDeclarationOrder.Add(controlId);
        }
    }

    private static void HandleEventWiring(AssignmentExpressionSyntax addAssign, DesignerParseResult result)
    {
        // this.X.EventName += new System.EventHandler(this.Handler);   |   this.X.EventName += this.Handler;
        // this.EventName   += ...                                      (event on the Form/UserControl itself)
        string? controlId = null;
        string eventName;

        if (addAssign.Left is MemberAccessExpressionSyntax outerMa)
        {
            eventName = outerMa.Name.Identifier.Text;
            if (outerMa.Expression is MemberAccessExpressionSyntax innerMa && innerMa.Expression is ThisExpressionSyntax)
                controlId = innerMa.Name.Identifier.Text;
            else if (outerMa.Expression is not ThisExpressionSyntax)
                return;
        }
        else
        {
            return;
        }

        string? handlerMethod = null;
        if (addAssign.Right is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 } oce)
        {
            if (TryGetThisMemberName(oce.ArgumentList!.Arguments[0].Expression, out var h)) handlerMethod = h;
        }
        else if (TryGetThisMemberName(addAssign.Right, out var direct))
        {
            handlerMethod = direct;
        }

        if (handlerMethod == null) return;

        if (controlId == null)
        {
            result.RootEventHandlers[eventName] = handlerMethod;
            return;
        }

        if (!result.ControlEventHandlers.TryGetValue(controlId, out var handlers))
        {
            handlers = new Dictionary<string, string>();
            result.ControlEventHandlers[controlId] = handlers;
        }
        handlers[eventName] = handlerMethod;
    }

    private static void HandleControlsAddInvocation(InvocationExpressionSyntax invoke, DesignerParseResult result)
    {
        // this.Controls.Add(this.X);         -> parent = root
        // this.panel1.Controls.Add(this.X);  -> parent = panel1
        if (invoke.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Add" } addAccess) return;
        if (addAccess.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Controls" } controlsAccess) return;

        string parentId;
        if (controlsAccess.Expression is ThisExpressionSyntax) parentId = "";
        else if (TryGetThisMemberName(controlsAccess.Expression, out var pid)) parentId = pid;
        else return;

        if (invoke.ArgumentList.Arguments.Count == 0) return;
        if (!TryGetThisMemberName(invoke.ArgumentList.Arguments[0].Expression, out var childId)) return;

        result.ParentOf[childId] = parentId;
    }

    private static string CleanLiteral(string text) => text.Trim().Trim('"');
}
