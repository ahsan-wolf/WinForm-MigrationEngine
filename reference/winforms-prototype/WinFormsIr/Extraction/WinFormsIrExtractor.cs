using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Models;

namespace WebFormsMigrator.WinFormsAnalyzers
{
    public class WinFormsSourceUnit
    {
        public string DesignerSource { get; set; } = "";
        public string CodeBehindSource { get; set; } = "";
    }

    /// <summary>
    /// Entry point. Produces a PageIr for one WinForms Form or UserControl, in the
    /// same shape as complete.ir.json, from its *.Designer.cs + code-behind .cs
    /// source text.
    /// </summary>
    public static class WinFormsIrExtractor
    {
        /// <param name="main">The Form or UserControl to build a PageIr for.</param>
        /// <param name="customControlLookup">
        /// Optional: maps a custom control's simple class name (e.g.
        /// "ActorSearchControl") to its own designer+code-behind source, so its
        /// control tree is inlined as children of the ControlNodeIr that declares
        /// it, instead of being left as an opaque leaf node. Build this by scanning
        /// the project for every *.Designer.cs / *.cs pair under a UserControl base
        /// type.
        /// </param>
        /// <param name="maxRecursionDepth">Guards against custom controls that (incorrectly) reference each other cyclically.</param>
        public static PageIr Extract(
            WinFormsSourceUnit main,
            Dictionary<string, WinFormsSourceUnit>? customControlLookup = null,
            int maxRecursionDepth = 4)
        {
            var designer = WinFormsDesignerParser.Parse(main.DesignerSource);

            // Designer.cs partials often don't repeat the base-type clause; the
            // code-behind file (": Form" / ": UserControl") usually does, so prefer it.
            var baseTypeFromCodeBehind = TryGetBaseTypeName(main.CodeBehindSource);
            var baseTypeName = baseTypeFromCodeBehind ?? designer.BaseTypeName ?? "System.Windows.Forms.Form";

            var controlFieldNames = new HashSet<string>(designer.DeclaredControlTypes.Keys);
            var members = WinFormsMemberExtractor.Extract(main.CodeBehindSource, controlFieldNames);

            // Semantic label per control: keyword match against its Id, then its Text attribute.
            var controlSemantics = new Dictionary<string, string>();
            foreach (var id in designer.DeclaredControlTypes.Keys)
            {
                designer.ControlAttributes.TryGetValue(id, out var attrs);
                attrs?.TryGetValue("Text", out var text);
                var semantic = SemanticKeywords.Infer(id, text);
                if (semantic != null) controlSemantics[id] = semantic;
            }

            var root = BuildTree(designer, baseTypeName, controlSemantics, customControlLookup, maxRecursionDepth);
            var workflows = WinFormsWorkflowExtractor.Extract(designer, members, controlSemantics, main.CodeBehindSource);
            var navigations = WinFormsNavigationExtractor.Extract(designer, main.CodeBehindSource);

            return new PageIr
            {
                SchemaVersion = "0.1.0",
                Name = designer.ClassName,
                MasterPageName = null, // no WinForms equivalent of a Web Forms master page
                RootControl = root,
                Members = members,
                CssAssets = new List<CssAssetIr>(),
                JsAssets = new List<JsAssetIr>(),
                Navigations = navigations,
                Workflows = workflows
            };
        }

        private static string? TryGetBaseTypeName(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var classDecl = tree.GetCompilationUnitRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            return classDecl?.BaseList?.Types.FirstOrDefault()?.Type.ToString();
        }

        private static ControlNodeIr BuildTree(
            DesignerParseResult designer,
            string baseTypeName,
            Dictionary<string, string> controlSemantics,
            Dictionary<string, WinFormsSourceUnit>? customControlLookup,
            int remainingDepth)
        {
            var root = new ControlNodeIr
            {
                Id = designer.ClassName,
                TagName = baseTypeName,
                Attributes = new Dictionary<string, string>(designer.RootAttributes),
                Children = new List<ControlNodeIr>()
            };

            var nodesById = new Dictionary<string, ControlNodeIr>();

            var allIds = designer.ControlDeclarationOrder
                .Concat(designer.DeclaredControlTypes.Keys)
                .Distinct();

            foreach (var id in allIds)
            {
                if (!designer.DeclaredControlTypes.TryGetValue(id, out var tagName)) continue;

                var attributes = designer.ControlAttributes.TryGetValue(id, out var a)
                    ? new Dictionary<string, string>(a)
                    : new Dictionary<string, string>();

                if (designer.ControlEventHandlers.TryGetValue(id, out var handlers))
                {
                    foreach (var handlerEntry in handlers)
                        attributes[$"On{handlerEntry.Key}"] = handlerEntry.Value;
                }

                var node = new ControlNodeIr
                {
                    Id = id,
                    TagName = tagName,
                    Attributes = attributes,
                    Semantic = controlSemantics.TryGetValue(id, out var sem) ? sem : null,
                    Children = new List<ControlNodeIr>()
                };

                if (remainingDepth > 0 && customControlLookup != null)
                {
                    var simpleType = tagName.Contains('.') ? tagName[(tagName.LastIndexOf('.') + 1)..] : tagName;
                    if (customControlLookup.TryGetValue(simpleType, out var customSource))
                    {
                        var customIr = Extract(customSource, customControlLookup, remainingDepth - 1);
                        node.Children.AddRange(customIr.RootControl.Children);
                        // Note: the custom control's own Members/Workflows are NOT merged in here —
                        // call Extract() on it directly too if you need its own PageIr as well.
                    }
                }

                nodesById[id] = node;
            }

            foreach (var edge in designer.ParentOf)
            {
                var childId = edge.Key;
                var parentId = edge.Value;
                if (!nodesById.TryGetValue(childId, out var childNode)) continue;

                if (string.IsNullOrEmpty(parentId))
                {
                    root.Children.Add(childNode);
                }
                else if (nodesById.TryGetValue(parentId, out var parentNode))
                {
                    parentNode.Children.Add(childNode);
                }
                else
                {
                    root.Children.Add(childNode); // parent declared elsewhere (e.g. another partial) — fall back to root
                }
            }

            // Non-visual/never-added components (Timer, ToolTip, BindingSource, ...) still get surfaced
            // at the root so nothing extracted from the field list is silently dropped.
            foreach (var entry in nodesById)
            {
                if (!designer.ParentOf.ContainsKey(entry.Key))
                    root.Children.Add(entry.Value);
            }

            return root;
        }
    }
}
