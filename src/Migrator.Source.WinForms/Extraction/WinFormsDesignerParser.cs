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

    /// <summary>Control id -> resource key from <c>resources.ApplyResources(this.btn, "btn")</c>; "" is the form itself (key "$this").</summary>
    public Dictionary<string, string> ResourceKeys { get; } = new();

    /// <summary>InitializeComponent statements the parser did not understand (single-line text). Never silently dropped.</summary>
    public List<string> Unhandled { get; } = new();

    /// <summary>Fields that InitializeComponent creates with <c>new</c> (the controls and components of the form).</summary>
    internal HashSet<string> Instantiated { get; } = new();
}

/// <summary>Reads InitializeComponent() statement by statement (syntax only; nothing is compiled or run).</summary>
public static partial class WinFormsDesignerParser
{
    /// <summary>Layout bookkeeping calls that carry no migratable fact.</summary>
    private static readonly HashSet<string> IgnorableCalls = new()
    {
        "SuspendLayout", "ResumeLayout", "PerformLayout", "BeginInit", "EndInit"
    };

    /// <summary>
    /// Locals a decompiler (or an old IDE) leaves in InitializeComponent: an alias of a collection (`Control.ControlCollection controls = base.Controls;`) and
    /// arrays of children (`Control[] controlArray = new Control[] { this.a, this.b };`, reused for several collections). Resolved when they are added.
    /// </summary>
    private sealed class LocalScope
    {
        public Dictionary<string, List<string>> Aliases { get; } = new();
        public Dictionary<string, List<ExpressionSyntax>> Arrays { get; } = new();

        /// <summary>The table style whose GridColumnStyles collection was aliased last, and the column arrays declared after it (see AttachImpliedColumns).</summary>
        public string? ColumnStyleOwner { get; set; }
        public List<(string Owner, List<string> Columns)> ImpliedColumns { get; } = new();
    }

    /// <param name="designerSourceText">A Designer.cs partial, or a whole code-behind file that also holds InitializeComponent (single-file forms).</param>
    public static DesignerParseResult Parse(string designerSourceText)
    {
        var result = new DesignerParseResult();
        var root = CSharpSyntaxTree.ParseText(designerSourceText).GetCompilationUnitRoot();

        // The class that owns InitializeComponent is the form (a single-file form may sit among helper classes); else the first class.
        var allClasses = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
        var classDecl = allClasses.FirstOrDefault(c => c.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "InitializeComponent"))
                        ?? allClasses.FirstOrDefault();
        if (classDecl == null) return result;

        // A Designer.cs holds only InitializeComponent and Dispose; any other method means the designer code shares the class with the code-behind,
        // so a field is a control only when InitializeComponent creates it (the rest are the form's own fields).
        var mixed = classDecl.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text is not ("InitializeComponent" or "Dispose"));

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
        var scope = new LocalScope();
        foreach (var stmt in initMethod.Body.Statements)
        {
            if (!HandleStatement(stmt, result, locals, scope))
                result.Unhandled.Add(Describe(stmt));
        }

        AttachImpliedColumns(result, scope);
        HoistTableStyleColumns(result);
        if (mixed) DropFieldsThatAreNotControls(result);
        return result;
    }

    /// <summary>Returns false when the statement is not understood (the caller records it).</summary>
    private static bool HandleStatement(StatementSyntax stmt, DesignerParseResult result, Dictionary<string, Dictionary<string, string>> locals, LocalScope scope)
    {
        switch (stmt)
        {
            case LocalDeclarationStatementSyntax local:
                // e.g. ComponentResourceManager resources = ...; DataGridViewCellStyle dataGridViewCellStyle1 = new ...;
                foreach (var v in local.Declaration.Variables)
                {
                    var init = v.Initializer?.Value;
                    if (init is not null && TryRememberLocal(v.Identifier.Text, init, scope, result)) continue;
                    locals.TryAdd(v.Identifier.Text, new Dictionary<string, string>());
                }
                return true;

            case ExpressionStatementSyntax { Expression: var expr }:
                return expr switch
                {
                    AssignmentExpressionSyntax { Left: IdentifierNameSyntax reused } a when a.IsKind(SyntaxKind.SimpleAssignmentExpression) && TryRememberLocal(reused.Identifier.Text, a.Right, scope, result) => true,
                    AssignmentExpressionSyntax a when a.IsKind(SyntaxKind.SimpleAssignmentExpression) => HandleAssignment(a, result, locals),
                    AssignmentExpressionSyntax a when a.IsKind(SyntaxKind.AddAssignmentExpression) => HandleEventWiring(a, result),
                    InvocationExpressionSyntax invoke => HandleInvocation(invoke, result, scope),
                    _ => false
                };

            case EmptyStatementSyntax:
                return true; // a stray `;` left by a decompiler

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
            case BaseExpressionSyntax: root = "this"; return true; // decompiled code writes base.Controls, base.Name, base.ClientSize
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
                    result.Instantiated.Add(first);
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

    private static bool HandleInvocation(InvocationExpressionSyntax invoke, DesignerParseResult result, LocalScope scope)
    {
        var methodName = invoke.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => ""
        };
        if (IgnorableCalls.Contains(methodName)) return true;
        if (methodName == "ApplyResources") return HandleApplyResources(invoke, result);

        if (invoke.Expression is not MemberAccessExpressionSyntax access) return false;
        if (!TryFlatten(access.Expression, out var root, out var receiver)) return false;
        if (root != "this")
        {
            // `controls.AddRange(controlArray)`: the local stands for a collection of the form or of a control.
            if (!scope.Aliases.TryGetValue(root, out var aliased)) return false;
            receiver = aliased.Concat(receiver).ToList();
        }

        // this.tlp.SetColumnSpan(this.child, 2)  (also SetRowSpan / SetColumn / SetRow)
        if (methodName is "SetColumn" or "SetRow" or "SetColumnSpan" or "SetRowSpan" && receiver.Count == 1)
            return HandleTableCell(invoke, methodName, result);

        // this.toolTip1.SetToolTip(this.btn, "text")  -> ToolTip attribute on the target control
        if (methodName == "SetToolTip" && receiver.Count == 1) return HandleSetToolTip(invoke, result);

        // this.il.Images.SetKeyName(0, "folder.png") -> Images.Keys (JSON string array; the position is the image index)
        if (methodName == "SetKeyName" && receiver is [var imageListId, "Images"]) return HandleImageKey(invoke, imageListId, result);

        // this.txt.DataBindings.Add(new Binding("Text", this.bindingSource1, "Name", ...))
        if (methodName == "Add" && receiver is [var boundId, "DataBindings"]) return HandleDataBinding(invoke, boundId, result);

        // this.tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F))
        if (methodName == "Add" && receiver is [var tableId, "ColumnStyles" or "RowStyles"])
            return HandleTableStyle(invoke, tableId, receiver[1], result);

        if (methodName is "Add" or "AddRange") return HandleCollectionAdd(invoke, receiver, methodName == "AddRange", result, scope);

        return false;
    }

    /// <summary>
    /// this.Controls.Add(this.X) | this.P.Controls.Add(this.X) | this.P.Items/DropDownItems/TabPages/Columns.Add|AddRange(...)
    /// | this.S.Panel1/Panel2.Controls.Add(this.X). Receiver = member chain after `this`.
    /// </summary>
    private static bool HandleCollectionAdd(InvocationExpressionSyntax invoke, List<string> receiver, bool isRange, DesignerParseResult result, LocalScope scope)
    {
        string parentId;
        string slot;
        switch (receiver)
        {
            case ["Controls"]:
                parentId = ""; slot = ""; break;
            case [var p, "Controls"]:
                parentId = p; slot = ""; break;
            case [var p, "Items" or "DropDownItems" or "TabPages" or "Columns" or "TableStyles"]:
                parentId = p; slot = receiver[1]; break;
            // Pre-ToolStrip menus: MainMenu.MenuItems are the top level, MenuItem.MenuItems the drop-down; named like the ToolStrip slots so targets need no change.
            case [var p, "MenuItems"]:
                var ownerType = result.DeclaredControlTypes.GetValueOrDefault(p, "");
                parentId = p;
                slot = ownerType.EndsWith("MainMenu", StringComparison.Ordinal) || ownerType.EndsWith("ContextMenu", StringComparison.Ordinal) ? "Items" : "DropDownItems";
                break;
            // DataGrid table style columns; hoisted onto the grid once the whole method is read.
            case [var p, "GridColumnStyles"]:
                parentId = p; slot = "Columns"; break;
            case [var p, "Panel1" or "Panel2", "Controls"]:
                parentId = p; slot = receiver[1]; break;
            default:
                return false;
        }

        var children = new List<string>();
        var literals = new List<string>(); // ComboBox/ListBox/column Items: string literals, not controls
        var args = invoke.ArgumentList.Arguments;

        bool Collect(ExpressionSyntax e)
        {
            if (TryGetThisMemberName(e, out var childId)) { children.Add(childId); return true; }
            if (slot == "Items" && e is LiteralExpressionSyntax lit) { literals.Add(lit.Token.ValueText); return true; }
            return false;
        }

        if (isRange)
        {
            var elements = args.Count == 1
                ? args[0].Expression switch
                {
                    ArrayCreationExpressionSyntax arr => arr.Initializer?.Expressions,
                    ImplicitArrayCreationExpressionSyntax arr => arr.Initializer.Expressions,
                    IdentifierNameSyntax local when scope.Arrays.TryGetValue(local.Identifier.Text, out var items) => SyntaxFactory.SeparatedList(items),
                    _ => null
                }
                : null;
            if (elements is null) return false;
            foreach (var e in elements)
                if (!Collect(e)) return false;
        }
        else
        {
            // Only the first argument is the child (Controls.Add(child, col, row) carries cell coordinates after it).
            if (args.Count == 0 || !Collect(args[0].Expression)) return false;
        }

        if (literals.Count > 0)
        {
            var itemAttrs = Attrs(result, parentId);
            var items = itemAttrs.TryGetValue("Items", out var existingItems)
                ? JsonSerializer.Deserialize<List<string>>(existingItems) ?? new List<string>()
                : new List<string>();
            items.AddRange(literals);
            itemAttrs["Items"] = JsonSerializer.Serialize(items);
            Touch(result, parentId);
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
        return children.Count > 0 || literals.Count > 0;
    }

    /// <summary>resources.ApplyResources(this.btn, "btn") / resources.ApplyResources(this, "$this").</summary>
    private static bool HandleApplyResources(InvocationExpressionSyntax invoke, DesignerParseResult result)
    {
        var args = invoke.ArgumentList.Arguments;
        if (args.Count != 2 || args[1].Expression is not LiteralExpressionSyntax key) return false;

        if (args[0].Expression is ThisExpressionSyntax) result.ResourceKeys[""] = key.Token.ValueText;
        else if (TryGetThisMemberName(args[0].Expression, out var controlId)) result.ResourceKeys[controlId] = key.Token.ValueText;
        else return false;
        return true;
    }

    private static bool HandleSetToolTip(InvocationExpressionSyntax invoke, DesignerParseResult result)
    {
        var args = invoke.ArgumentList.Arguments;
        if (args.Count != 2 || !TryGetThisMemberName(args[0].Expression, out var controlId) || args[1].Expression is not LiteralExpressionSyntax text)
            return false;

        Attrs(result, controlId)["ToolTip"] = text.Token.ValueText;
        Touch(result, controlId);
        return true;
    }

    private static bool HandleImageKey(InvocationExpressionSyntax invoke, string imageListId, DesignerParseResult result)
    {
        var args = invoke.ArgumentList.Arguments;
        if (args.Count != 2 || args[0].Expression is not LiteralExpressionSyntax { Token.Value: int index } || args[1].Expression is not LiteralExpressionSyntax key || index < 0 || index > 4096)
            return false;

        var attrs = Attrs(result, imageListId);
        var keys = attrs.TryGetValue("Images.Keys", out var json) ? JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>() : new List<string>();
        while (keys.Count <= index) keys.Add("");
        keys[index] = key.Token.ValueText;
        attrs["Images.Keys"] = JsonSerializer.Serialize(keys);
        Touch(result, imageListId);
        return true;
    }

    /// <summary>DataBinding.{Property} = {source}.{member}, e.g. DataBinding.Text = bindingSource1.Name.</summary>
    private static bool HandleDataBinding(InvocationExpressionSyntax invoke, string controlId, DesignerParseResult result)
    {
        var args = invoke.ArgumentList.Arguments;
        if (args.Count != 1 || args[0].Expression is not ObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: >= 3 } ctor })
            return false;
        if (ctor[0].Expression is not LiteralExpressionSyntax property || ctor[2].Expression is not LiteralExpressionSyntax member)
            return false;

        var source = TryGetThisMemberName(ctor[1].Expression, out var sourceId) ? sourceId : ctor[1].Expression.ToString();
        Attrs(result, controlId)[$"DataBinding.{property.Token.ValueText}"] = $"{source}.{member.Token.ValueText}";
        Touch(result, controlId);
        return true;
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

    /// <summary>
    /// Remembers a local the decompiler introduced: an alias of a collection of this form (`base.Controls`, `this.panel1.Controls`, `this.mnuFile.MenuItems`) or an array
    /// of children. A later assignment to the same name replaces the earlier value. False when the value is neither.
    /// </summary>
    private static bool TryRememberLocal(string name, ExpressionSyntax value, LocalScope scope, DesignerParseResult result)
    {
        switch (value)
        {
            case ArrayCreationExpressionSyntax { Initializer: { } init }:
                scope.Arrays[name] = init.Expressions.ToList();
                RememberColumnArray(init.Expressions, scope, result);
                return true;
            case ImplicitArrayCreationExpressionSyntax implicitArray:
                scope.Arrays[name] = implicitArray.Initializer.Expressions.ToList();
                return true;
            case MemberAccessExpressionSyntax access when TryFlatten(access, out var root, out var chain) && root == "this" && chain.Count >= 1
                                                         && chain[^1] is "Controls" or "MenuItems" or "Items" or "DropDownItems" or "TabPages" or "Columns" or "GridColumnStyles" or "TableStyles":
                scope.Aliases[name] = chain;
                if (chain.Count >= 2 && chain[^1] == "GridColumnStyles") scope.ColumnStyleOwner = chain[^2];
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A column array declared right after the alias of a table style's GridColumnStyles belongs to that style, even where the `AddRange` call itself is missing
    /// (the original comments it out): its columns are attached afterwards, unless something already attached them.
    /// </summary>
    private static void RememberColumnArray(SeparatedSyntaxList<ExpressionSyntax> elements, LocalScope scope, DesignerParseResult result)
    {
        if (scope.ColumnStyleOwner is not { } owner || elements.Count == 0) return;
        var ids = new List<string>();
        foreach (var e in elements)
        {
            if (!TryGetThisMemberName(e, out var id) || !result.DeclaredControlTypes.GetValueOrDefault(id, "").EndsWith("Column", StringComparison.Ordinal)) return;
            ids.Add(id);
        }
        scope.ImpliedColumns.Add((owner, ids));
    }

    private static void AttachImpliedColumns(DesignerParseResult result, LocalScope scope)
    {
        foreach (var (owner, columns) in scope.ImpliedColumns)
            foreach (var id in columns.Where(c => !result.ParentOf.TryGetValue(c, out var parent) || parent.Length == 0))
            {
                result.ParentOf[id] = owner;
                Attrs(result, id)["ParentSlot"] = "Columns";
            }
    }

    /// <summary>
    /// A DataGrid keeps its columns in a table style (`grid.TableStyles.Add(style)`, `style.GridColumnStyles.AddRange(...)`). The columns are moved onto the grid, as the
    /// columns of a DataGridView are, with the style's mapping name as the column's data property; the style itself is not a control and is dropped.
    /// </summary>
    private static void HoistTableStyleColumns(DesignerParseResult result)
    {
        var styles = result.ParentOf.Where(kv => Attrs(result, kv.Key).GetValueOrDefault("ParentSlot") == "TableStyles").ToList();
        foreach (var (style, grid) in styles)
        {
            foreach (var column in result.ParentOf.Where(kv => kv.Value == style).Select(kv => kv.Key).ToList())
            {
                result.ParentOf[column] = grid;
                var attrs = Attrs(result, column);
                if (!attrs.ContainsKey("DataPropertyName") && attrs.TryGetValue("MappingName", out var mapping)) attrs["DataPropertyName"] = mapping;
            }
            result.ParentOf.Remove(style);
            result.DeclaredControlTypes.Remove(style);
            result.ControlAttributes.Remove(style);
            result.ControlEventHandlers.Remove(style);
            result.ControlDeclarationOrder.Remove(style);
        }
    }

    /// <summary>Single-file forms: a field InitializeComponent never creates is one of the form's own fields, not a control (it stays a member).</summary>
    private static void DropFieldsThatAreNotControls(DesignerParseResult result)
    {
        foreach (var name in result.DeclaredControlTypes.Keys.Where(n => !result.Instantiated.Contains(n)).ToList())
        {
            result.DeclaredControlTypes.Remove(name);
            result.ControlAttributes.Remove(name);
            result.ControlEventHandlers.Remove(name);
            result.ControlDeclarationOrder.Remove(name);
            result.ParentOf.Remove(name);
        }
    }
}
