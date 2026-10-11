using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Facet "controlTree": populates <see cref="PageIr.Name"/> and <see cref="PageIr.RootControl"/> from the Designer.cs
/// (<c>MarkupContent</c>) and code-behind (<c>CodeBehindContent</c>). Only real controls become nodes (no anonymous noise).
/// </summary>
public sealed class WinFormsControlTreeExtractor : IIrExtractor
{
    private readonly IReadOnlyDictionary<string, WinFormsSourceText> _userControls;
    private readonly int _maxRecursionDepth;

    /// <param name="userControls">
    /// Simple class name -> source of every UserControl in the project. A field whose type matches is inlined as real
    /// child nodes instead of an opaque leaf.
    /// </param>
    /// <param name="maxRecursionDepth">Guards against UserControls that (incorrectly) reference each other cyclically.</param>
    public WinFormsControlTreeExtractor(IReadOnlyDictionary<string, WinFormsSourceText>? userControls = null, int maxRecursionDepth = 4)
    {
        _userControls = userControls ?? new Dictionary<string, WinFormsSourceText>();
        _maxRecursionDepth = maxRecursionDepth;
    }

    public string Facet => "controlTree";

    public void Extract(IrExtractionContext context, PageIr target)
    {
        var designer = WinFormsDesignerParser.Parse(context.MarkupContent);
        target.Name = designer.ClassName;
        target.MasterPageName = null; // no WinForms equivalent of a Web Forms master page
        target.RootControl = BuildRoot(designer, context.CodeBehindContent ?? "", _maxRecursionDepth);
    }

    private ControlNodeIr BuildRoot(DesignerParseResult designer, string codeBehind, int remainingDepth)
    {
        // Designer.cs partials rarely repeat the base-type clause; the code-behind usually does, so it wins.
        var baseType = CodeBehind.FindClass(codeBehind, designer.ClassName)?.BaseList?.Types.FirstOrDefault()?.Type.ToString()
                       ?? designer.BaseTypeName
                       ?? "Form";

        var root = new ControlNodeIr
        {
            Id = designer.ClassName,
            TagName = CodeBehind.NormalizeBaseType(baseType),
            Attributes = new Dictionary<string, string>(designer.RootAttributes)
        };
        foreach (var (eventName, handler) in designer.RootEventHandlers)
            root.Attributes[$"On{eventName}"] = handler;

        var nodesById = new Dictionary<string, ControlNodeIr>();
        foreach (var id in designer.ControlDeclarationOrder.Concat(designer.DeclaredControlTypes.Keys).Distinct())
        {
            if (!designer.DeclaredControlTypes.TryGetValue(id, out var tagName)) continue;

            var attributes = designer.ControlAttributes.TryGetValue(id, out var a)
                ? new Dictionary<string, string>(a)
                : new Dictionary<string, string>();
            if (designer.ControlEventHandlers.TryGetValue(id, out var handlers))
                foreach (var (eventName, handler) in handlers)
                    attributes[$"On{eventName}"] = handler;

            var node = new ControlNodeIr { Id = id, TagName = tagName, Attributes = attributes };

            if (remainingDepth > 0)
            {
                var simpleType = tagName[(tagName.LastIndexOf('.') + 1)..];
                if (_userControls.TryGetValue(simpleType, out var source))
                {
                    var inner = WinFormsDesignerParser.Parse(source.DesignerSource);
                    node.Children.AddRange(BuildRoot(inner, source.CodeBehindSource, remainingDepth - 1).Children);
                }
            }

            nodesById[id] = node;
        }

        foreach (var (childId, parentId) in designer.ParentOf)
        {
            if (!nodesById.TryGetValue(childId, out var child)) continue;

            if (parentId.Length > 0 && nodesById.TryGetValue(parentId, out var parent))
                parent.Children.Add(child);
            else
                root.Children.Add(child); // parent is the form itself, or declared elsewhere (another partial)
        }

        // Non-visual components never added to a container (Timer, ToolTip, BindingSource, ...) still surface at the root.
        foreach (var (id, node) in nodesById)
            if (!designer.ParentOf.ContainsKey(id))
                root.Children.Add(node);

        return root;
    }
}
