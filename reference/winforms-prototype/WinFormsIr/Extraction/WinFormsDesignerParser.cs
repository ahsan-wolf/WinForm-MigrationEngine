using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace WebFormsMigrator.WinFormsAnalyzers
{
    public class DesignerParseResult
    {
        /// <summary>Name of the partial class found in the Designer.cs file (the Form/UserControl itself).</summary>
        public string ClassName { get; set; } = "";

        /// <summary>Base type as declared on THIS file's class decl, if any (Designer.cs partials often omit it).</summary>
        public string? BaseTypeName { get; set; }

        /// <summary>Control field name -&gt; declared/instantiated type, e.g. "txtSearch" -&gt; "System.Windows.Forms.TextBox".</summary>
        public Dictionary<string, string> DeclaredControlTypes { get; } = new();

        /// <summary>Control field name -&gt; { PropertyName -&gt; assigned value text }.</summary>
        public Dictionary<string, Dictionary<string, string>> ControlAttributes { get; } = new();

        /// <summary>Control field name -&gt; { EventName -&gt; handler method name }.</summary>
        public Dictionary<string, Dictionary<string, string>> ControlEventHandlers { get; } = new();

        /// <summary>Child control id -&gt; parent control id ("" means the parent is the Form/UserControl root itself).</summary>
        public Dictionary<string, string> ParentOf { get; } = new();

        /// <summary>Controls in first-seen order within InitializeComponent, used later to order workflow steps.</summary>
        public List<string> ControlDeclarationOrder { get; } = new();

        /// <summary>Form/UserControl-level property assignments, e.g. this.Text = "Actor Management";</summary>
        public Dictionary<string, string> RootAttributes { get; } = new();
    }

    public static class WinFormsDesignerParser
    {
        public static DesignerParseResult Parse(string designerSourceText)
        {
            var result = new DesignerParseResult();
            var tree = CSharpSyntaxTree.ParseText(designerSourceText);
            var root = tree.GetCompilationUnitRoot();

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
                var expr = exprStmt.Expression;

                if (expr is AssignmentExpressionSyntax assign)
                {
                    if (assign.IsKind(SyntaxKind.SimpleAssignmentExpression))
                        HandleAssignment(assign, result);
                    else if (assign.IsKind(SyntaxKind.AddAssignmentExpression))
                        HandleEventWiring(assign, result);
                }
                else if (expr is InvocationExpressionSyntax invoke)
                {
                    HandleControlsAddInvocation(invoke, result);
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

            // Case A: this.X = new SomeControlType(...);
            if (TryGetThisMemberName(left, out var directId))
            {
                if (assign.Right is ObjectCreationExpressionSyntax creation)
                {
                    result.DeclaredControlTypes[directId] = creation.Type.ToString();
                    if (!result.ControlAttributes.ContainsKey(directId))
                        result.ControlAttributes[directId] = new Dictionary<string, string>();
                    if (!result.ControlEventHandlers.ContainsKey(directId))
                        result.ControlEventHandlers[directId] = new Dictionary<string, string>();
                    if (!result.ControlDeclarationOrder.Contains(directId))
                        result.ControlDeclarationOrder.Add(directId);
                }
                else
                {
                    // this.Name = "ActorManagementForm";  this.Text = "Actor Management";
                    result.RootAttributes[directId] = CleanLiteral(assign.Right.ToString());
                }
                return;
            }

            // Case B: this.X.SomeProperty = value;   (attribute on a previously-declared control)
            if (left is MemberAccessExpressionSyntax outerMa &&
                outerMa.Expression is MemberAccessExpressionSyntax innerMa &&
                innerMa.Expression is ThisExpressionSyntax)
            {
                var controlId = innerMa.Name.Identifier.Text;
                var propName = outerMa.Name.Identifier.Text;
                var value = CleanLiteral(assign.Right.ToString());

                if (!result.ControlAttributes.TryGetValue(controlId, out var attrs))
                {
                    attrs = new Dictionary<string, string>();
                    result.ControlAttributes[controlId] = attrs;
                }
                attrs[propName] = value;

                if (!result.ControlDeclarationOrder.Contains(controlId))
                    result.ControlDeclarationOrder.Add(controlId);
            }
        }

        private static void HandleEventWiring(AssignmentExpressionSyntax addAssign, DesignerParseResult result)
        {
            // this.X.EventName += new System.EventHandler(this.Handler);
            // this.X.EventName += this.Handler;
            if (addAssign.Left is not MemberAccessExpressionSyntax outerMa ||
                outerMa.Expression is not MemberAccessExpressionSyntax innerMa ||
                innerMa.Expression is not ThisExpressionSyntax)
            {
                return;
            }

            var controlId = innerMa.Name.Identifier.Text;
            var eventName = outerMa.Name.Identifier.Text;
            string? handlerMethod = null;

            if (addAssign.Right is ObjectCreationExpressionSyntax oce &&
                oce.ArgumentList != null && oce.ArgumentList.Arguments.Count > 0)
            {
                if (TryGetThisMemberName(oce.ArgumentList.Arguments[0].Expression, out var h))
                    handlerMethod = h;
            }
            else if (TryGetThisMemberName(addAssign.Right, out var direct))
            {
                handlerMethod = direct;
            }

            if (handlerMethod == null) return;

            if (!result.ControlEventHandlers.TryGetValue(controlId, out var handlers))
            {
                handlers = new Dictionary<string, string>();
                result.ControlEventHandlers[controlId] = handlers;
            }
            handlers[eventName] = handlerMethod;
        }

        private static void HandleControlsAddInvocation(InvocationExpressionSyntax invoke, DesignerParseResult result)
        {
            // this.Controls.Add(this.X);            -> parent = root
            // this.panel1.Controls.Add(this.X);     -> parent = panel1
            if (invoke.Expression is not MemberAccessExpressionSyntax addAccess ||
                addAccess.Name.Identifier.Text != "Add")
            {
                return;
            }

            if (addAccess.Expression is not MemberAccessExpressionSyntax controlsAccess ||
                controlsAccess.Name.Identifier.Text != "Controls")
            {
                return;
            }

            string parentId;
            if (controlsAccess.Expression is ThisExpressionSyntax)
            {
                parentId = "";
            }
            else if (TryGetThisMemberName(controlsAccess.Expression, out var pid))
            {
                parentId = pid;
            }
            else
            {
                return;
            }

            if (invoke.ArgumentList.Arguments.Count == 0) return;
            if (!TryGetThisMemberName(invoke.ArgumentList.Arguments[0].Expression, out var childId)) return;

            result.ParentOf[childId] = parentId;
        }

        private static string CleanLiteral(string text) => text.Trim().Trim('"');
    }
}
