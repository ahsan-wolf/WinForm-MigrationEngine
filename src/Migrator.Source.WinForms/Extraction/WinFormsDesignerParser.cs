using System.Text.Json;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Control field name -> { PropertyName -> assigned value text }. Besides real properties this holds designer facts that
    /// have no property of their own, under dotted keys that cannot collide with a C# property name:
    /// <c>ParentSlot</c>, <c>TableLayout.*</c>, <c>DataBinding.*</c>, <c>Items</c>, <c>ColumnStyles</c>/<c>RowStyles</c>.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> ControlAttributes { get; } = new();

    /// <summary>Control field name -> { EventName -> handler method name }.</summary>
    public Dictionary<string, Dictionary<string, string>> ControlEventHandlers { get; } = new();

    /// <summary>Events wired on the Form/UserControl itself (this.Load += ...) -> handler method name.</summary>
    public Dictionary<string, string> RootEventHandlers { get; } = new();

    /// <summary>Child control id -> parent control id ("" means the parent is the Form/UserControl root). Insertion order = add order.</summary>
    public Dictionary<string, string> ParentOf { get; } = new();

    /// <summary>Controls in first-seen order within InitializeComponent.</summary>
    public List<string> ControlDeclarationOrder { get; } = new();

    /// <summary>Form/UserControl-level property assignments, e.g. this.Text = "Actor Management";</summary>
    public Dictionary<string, string> RootAttributes { get; } = new();

    /// <summary>InitializeComponent statements the parser did not understand (single-line text). Never silently dropped.</summary>
    public List<string> Unhandled { get; } = new();
}

/// <summary>Reads InitializeComponent() statement by statement (syntax only; nothing is compiled or run).</summary>
public static partial class WinFormsDesignerParser
{
    /// <summary>Layout bookkeeping calls that carry no migratable fact.</summary>
    private static readonly HashSet<string> IgnorableCalls = new()
    {
        "SuspendLayout", "ResumeLayout", "PerformLayout", "BeginInit", "EndInit", "ApplyResources"
    };

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

        var locals = new Dictionary<string, Dictionary<string, string>>(); // designer locals, e.g. DataGridViewCellStyle
        foreach (var stmt in initMethod.Body.Statements)
        {
            if (!HandleStatement(stmt, result, locals))
                result.Unhandled.Add(Describe(stmt));
        }

        return result;
    }

    /// <summary>Returns false when the statement is not understood (the caller records it).</summary>
    private static bool HandleStatement(StatementSyntax stmt, DesignerParseResult result, Dictionary<string, Dictionary<string, string>> locals)
    {
        switch (stmt)
        {
            case LocalDeclarationStatementSyntax local:
                // e.g. ComponentResourceManager resources = ...; DataGridViewCellStyle dataGridViewCellStyle1 = new ...;
                foreach (var v in local.Declaration.Variables) locals.TryAdd(v.Identifier.Text, new Dictionary<string, string>());
                return true;

            case ExpressionStatementSyntax { Expression: var expr }:
                return expr switch
                {
                    AssignmentExpressionSyntax a when a.IsKind(SyntaxKind.SimpleAssignmentExpression) => HandleAssignment(a, result, locals),
                    AssignmentExpressionSyntax a when a.IsKind(SyntaxKind.AddAssignmentExpression) => HandleEventWiring(a, result),
                    InvocationExpressionSyntax invoke => HandleInvocation(invoke, result),
                    _ => false
                };

            default:
                return false;
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Flattens a member-access chain: <c>this.dgv.Columns</c> -> root "this", segments [dgv, Columns];
    /// <c>style.Format</c> -> root "style", segments [Format]. False when the chain does not start at `this` or an identifier.
    /// </summary>
    private static bool TryFlatten(ExpressionSyntax expr, out string root, out List<string> segments)
    {
        segments = new List<string>();
        root = "";
        while (expr is MemberAccessExpressionSyntax ma)
        {
            segments.Insert(0, ma.Name.Identifier.Text);
            expr = ma.Expression;
        }

        switch (expr)
        {
            case ThisExpressionSyntax: root = "this"; return true;
            case IdentifierNameSyntax id: root = id.Identifier.Text; return true;
            default: return false;
        }
    }

    /// <summary>Matches `this.X` and returns X.</summary>
    private static bool TryGetThisMemberName(ExpressionSyntax expr, out string name)
    {
        if (expr is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } ma)
        {
            name = ma.Name.Identifier.Text;
            return true;
        }
        name = "";
        return false;
    }

    private static Dictionary<string, string> Attrs(DesignerParseResult result, string controlId)
    {
        if (!result.ControlAttributes.TryGetValue(controlId, out var attrs))
        {
            attrs = new Dictionary<string, string>();
            result.ControlAttributes[controlId] = attrs;
        }
        return attrs;
    }

    private static void Touch(DesignerParseResult result, string controlId)
    {
        if (!result.ControlDeclarationOrder.Contains(controlId)) result.ControlDeclarationOrder.Add(controlId);
    }

    private static string Describe(SyntaxNode stmt)
    {
        var text = WhitespaceRegex().Replace(stmt.ToString(), " ").Trim();
        return text.Length <= 200 ? text : text[..200] + "...";
    }

    private static string CleanLiteral(string text) => text.Trim().Trim('"');

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    // ---- assignments ---------------------------------------------------------------------------------------------

    private static bool HandleAssignment(AssignmentExpressionSyntax assign, DesignerParseResult result, Dictionary<string, Dictionary<string, string>> locals)
    {
        if (!TryFlatten(assign.Left, out var root, out var segments)) return false;

        // this.X = ...  /  this.X.Prop[.Sub] = ...
        if (root == "this")
        {
            if (segments.Count == 0) return false;
            var first = segments[0];

            if (segments.Count == 1)
            {
                if (first == "components") return true; // IContainer plumbing

                // `new` only declares a control when X is a field of the class; this.ClientSize = new Size(..),
                // this.AutoScaleDimensions = new SizeF(..) are properties of the Form/UserControl itself.
                if (assign.Right is ObjectCreationExpressionSyntax creation && result.DeclaredControlTypes.ContainsKey(first))
                {
                    result.DeclaredControlTypes[first] = creation.Type.ToString();
                    Attrs(result, first);
                    result.ControlEventHandlers.TryAdd(first, new Dictionary<string, string>());
                    Touch(result, first);
                }
                else
                {
                    result.RootAttributes[first] = CleanLiteral(assign.Right.ToString());
                }
                return true;
            }

            var controlId = first;
            var property = string.Join('.', segments.Skip(1));
            var attrs = Attrs(result, controlId);
            Touch(result, controlId);

            // this.col.DefaultCellStyle = dataGridViewCellStyle1;  -> copy the local's properties as DefaultCellStyle.*
            if (assign.Right is IdentifierNameSyntax local && locals.TryGetValue(local.Identifier.Text, out var localProps))
            {
                foreach (var (k, v) in localProps) attrs[$"{property}.{k}"] = v;
                return true;
            }

            attrs[property] = CleanLiteral(assign.Right.ToString());
            return true;
        }

        // style.Format = "d";   (designer local such as a DataGridViewCellStyle)
        if (locals.TryGetValue(root, out var props) && segments.Count >= 1)
        {
            props[string.Join('.', segments)] = CleanLiteral(assign.Right.ToString());
            return true;
        }

        return false;
    }

    // ---- events --------------------------------------------------------------------------------------------------

    private static bool HandleEventWiring(AssignmentExpressionSyntax addAssign, DesignerParseResult result)
    {
        // this.X.EventName += new System.EventHandler(this.Handler);   |   this.X.EventName += this.Handler;
        // this.EventName   += ...                                      (event on the Form/UserControl itself)
        if (!TryFlatten(addAssign.Left, out var root, out var segments) || root != "this" || segments.Count is < 1 or > 2)
            return false;

        var eventName = segments[^1];
        var controlId = segments.Count == 2 ? segments[0] : null;

        string? handlerMethod = null;
        if (addAssign.Right is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 } oce)
        {
            if (TryGetThisMemberName(oce.ArgumentList!.Arguments[0].Expression, out var h)) handlerMethod = h;
        }
        else if (TryGetThisMemberName(addAssign.Right, out var direct))
        {
            handlerMethod = direct;
        }

        if (handlerMethod == null) return false;

        if (controlId == null)
        {
            result.RootEventHandlers[eventName] = handlerMethod;
            return true;
        }

        if (!result.ControlEventHandlers.TryGetValue(controlId, out var handlers))
        {
            handlers = new Dictionary<string, string>();
            result.ControlEventHandlers[controlId] = handlers;
        }
        handlers[eventName] = handlerMethod;
        return true;
    }

    // ---- invocations ---------------------------------------------------------------------------------------------

    private static bool HandleInvocation(InvocationExpressionSyntax invoke, DesignerParseResult result)
    {
        var methodName = invoke.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => ""
        };
        if (IgnorableCalls.Contains(methodName)) return true;

        if (invoke.Expression is not MemberAccessExpressionSyntax access) return false;
        if (!TryFlatten(access.Expression, out var root, out var receiver) || root != "this") return false;

        // this.tlp.SetColumnSpan(this.child, 2)  (also SetRowSpan / SetColumn / SetRow)
        if (methodName is "SetColumn" or "SetRow" or "SetColumnSpan" or "SetRowSpan" && receiver.Count == 1)
            return HandleTableCell(invoke, methodName, result);

        // this.tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F))
        if (methodName == "Add" && receiver is [var tableId, "ColumnStyles" or "RowStyles"])
            return HandleTableStyle(invoke, tableId, receiver[1], result);

        if (methodName is "Add" or "AddRange") return HandleCollectionAdd(invoke, receiver, methodName == "AddRange", result);

        return false;
    }

    /// <summary>
    /// this.Controls.Add(this.X) | this.P.Controls.Add(this.X) | this.P.Items/DropDownItems/TabPages/Columns.Add|AddRange(...)
    /// | this.S.Panel1/Panel2.Controls.Add(this.X). Receiver = member chain after `this`.
    /// </summary>
    private static bool HandleCollectionAdd(InvocationExpressionSyntax invoke, List<string> receiver, bool isRange, DesignerParseResult result)
    {
        string parentId;
        string slot;
        switch (receiver)
        {
            case ["Controls"]:
                parentId = ""; slot = ""; break;
            case [var p, "Controls"]:
                parentId = p; slot = ""; break;
            case [var p, "Items" or "DropDownItems" or "TabPages" or "Columns"]:
                parentId = p; slot = receiver[1]; break;
            case [var p, "Panel1" or "Panel2", "Controls"]:
                parentId = p; slot = receiver[1]; break;
            default:
                return false;
        }

        var children = new List<string>();
        var args = invoke.ArgumentList.Arguments;
        if (isRange)
        {
            var elements = args.Count == 1
                ? args[0].Expression switch
                {
                    ArrayCreationExpressionSyntax a => a.Initializer?.Expressions,
                    ImplicitArrayCreationExpressionSyntax a => a.Initializer.Expressions,
                    _ => null
                }
                : null;
            if (elements is null) return false;
            foreach (var e in elements)
            {
                if (!TryGetThisMemberName(e, out var childId)) return false;
                children.Add(childId);
            }
        }
        else
        {
            // Only the first argument is the child (Controls.Add(child, col, row) carries cell coordinates after it).
            if (args.Count == 0 || !TryGetThisMemberName(args[0].Expression, out var childId)) return false;
            children.Add(childId);
        }

        foreach (var child in children)
        {
            result.ParentOf[child] = parentId;
            if (slot.Length > 0) Attrs(result, child)["ParentSlot"] = slot;
        }

        // TableLayoutPanel: Controls.Add(child, column, row). Literal coordinates are recorded independently; anything
        // computed is reported instead of guessed.
        if (!isRange && slot.Length == 0 && args.Count == 3)
        {
            var attrs = Attrs(result, children[0]);
            var complete = true;
            if (int.TryParse(args[1].Expression.ToString(), out var column)) attrs["TableLayout.Column"] = column.ToString(); else complete = false;
            if (int.TryParse(args[2].Expression.ToString(), out var row)) attrs["TableLayout.Row"] = row.ToString(); else complete = false;
            if (!complete) result.Unhandled.Add("cell coordinates are not literals: " + Describe(invoke));
        }
        return children.Count > 0;
    }

    private static bool HandleTableCell(InvocationExpressionSyntax invoke, string methodName, DesignerParseResult result)
    {
        var args = invoke.ArgumentList.Arguments;
        if (args.Count != 2 || !TryGetThisMemberName(args[0].Expression, out var childId)
            || !int.TryParse(args[1].Expression.ToString(), out var value))
            return false;

        Attrs(result, childId)[$"TableLayout.{methodName["Set".Length..]}"] = value.ToString(); // Column | Row | ColumnSpan | RowSpan
        return true;
    }

    /// <summary>Accumulates a JSON array in the container's ColumnStyles / RowStyles attribute: [{"SizeType":"Percent","Value":50}].</summary>
    private static bool HandleTableStyle(InvocationExpressionSyntax invoke, string tableId, string kind, DesignerParseResult result)
    {
        var args = invoke.ArgumentList.Arguments;
        if (args.Count != 1 || args[0].Expression is not ObjectCreationExpressionSyntax { ArgumentList: { } ctorArgs } creation) return false;

        var parts = ctorArgs.Arguments;
        if (parts.Count > 2) return false;

        // new ColumnStyle() is WinForms' default: AutoSize.
        var sizeType = parts.Count == 0 ? "AutoSize" : parts[0].ToString();
        sizeType = sizeType[(sizeType.LastIndexOf('.') + 1)..];
        double? size = null;
        if (parts.Count == 2)
        {
            if (!double.TryParse(parts[1].ToString().TrimEnd('F', 'f'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                return false;
            size = number;
        }

        var attrs = Attrs(result, tableId);
        Touch(result, tableId);
        var list = attrs.TryGetValue(kind, out var existing)
            ? JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(existing) ?? new()
            : new List<Dictionary<string, object?>>();
        list.Add(new Dictionary<string, object?> { ["SizeType"] = sizeType, ["Value"] = size });
        attrs[kind] = JsonSerializer.Serialize(list);
        return true;
    }
}
