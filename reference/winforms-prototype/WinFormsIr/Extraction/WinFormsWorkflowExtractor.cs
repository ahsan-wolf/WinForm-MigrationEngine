using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Models;

namespace WebFormsMigrator.WinFormsAnalyzers
{
    /// <summary>
    /// For every control.Event -> HandlerMethod wiring found in the designer file,
    /// walks the handler's body (in the code-behind file) for direct calls to other
    /// known methods on the same class and produces a WorkflowIr.
    ///
    /// Two behaviors here are reverse-engineered from complete.ir.json rather than
    /// documented, and are worth re-checking against the real
    /// NavigationWorkflowExtractor source if you have it:
    ///   1. Workflow.Semantic = the *trigger control's own* inferred Semantic label
    ///      when one exists (e.g. btnSave -> "Persists form data (create or
    ///      update)"), falling back to "Runs N step(s): ..." only when the control
    ///      itself has no clear semantic (e.g. a GridView row-command handler that
    ///      covers both Edit and Delete).
    ///   2. Steps are ordered by the *declaration order of the called methods in
    ///      Members*, not by the order they're called within the handler body.
    /// </summary>
    public static class WinFormsWorkflowExtractor
    {
        public static List<WorkflowIr> Extract(
            DesignerParseResult designer,
            List<MemberIr> members,
            Dictionary<string, string> controlSemantics,
            string codeBehindSourceText)
        {
            var workflows = new List<WorkflowIr>();

            var methodNamesInDeclarationOrder = members
                .Where(m => m.Kind == (int)MemberKind.Method)
                .Select(m => m.Name)
                .ToList();

            var methodBodySources = ExtractMethodBodySources(codeBehindSourceText);

            foreach (var controlEntry in designer.ControlEventHandlers)
            {
                var controlId = controlEntry.Key;
                foreach (var handlerEntry in controlEntry.Value)
                {
                    var eventName = handlerEntry.Key;
                    var handlerMethod = handlerEntry.Value;

                    if (!methodBodySources.TryGetValue(handlerMethod, out var bodySource))
                        continue; // handler not found in this file (partial elsewhere, or base class)

                    var calledMethodNames = FindDirectlyCalledOwnMethods(bodySource, methodNamesInDeclarationOrder);
                    var orderedSteps = methodNamesInDeclarationOrder
                        .Where(calledMethodNames.Contains)
                        .Select(name => new WorkflowStepIr
                        {
                            MethodName = name,
                            Semantic = SemanticKeywords.Infer(name)
                        })
                        .ToList();

                    controlSemantics.TryGetValue(controlId, out var controlSemantic);
                    var semantic = controlSemantic ?? BuildFallbackSemantic(orderedSteps);

                    workflows.Add(new WorkflowIr
                    {
                        Name = $"{controlId}.{eventName}",
                        TriggerControlId = controlId,
                        TriggerEvent = eventName,
                        HandlerMethod = handlerMethod,
                        Steps = orderedSteps,
                        Semantic = semantic
                    });
                }
            }

            return workflows;
        }

        private static string BuildFallbackSemantic(List<WorkflowStepIr> steps)
        {
            if (steps.Count == 0) return "No delegated business logic detected";
            if (steps.Count == 1) return steps[0].Semantic ?? $"Calls {steps[0].MethodName}";
            return $"Runs {steps.Count} step(s): {string.Join(", ", steps.Select(s => s.MethodName))}";
        }

        private static Dictionary<string, string> ExtractMethodBodySources(string codeBehindSourceText)
        {
            var map = new Dictionary<string, string>();
            var tree = CSharpSyntaxTree.ParseText(codeBehindSourceText);
            var root = tree.GetCompilationUnitRoot();
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (method.Body != null)
                    map[method.Identifier.Text] = method.Body.ToString();
            }
            return map;
        }

        private static HashSet<string> FindDirectlyCalledOwnMethods(string methodBodySource, List<string> knownMethodNames)
        {
            var found = new HashSet<string>();

            // Wrap the isolated body text so it re-parses as a standalone method body.
            var wrapped = "class __Wrapper { void __M() " + methodBodySource + " }";
            var tree = CSharpSyntaxTree.ParseText(wrapped);

            foreach (var inv in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string? name = null;
                if (inv.Expression is IdentifierNameSyntax id)
                {
                    name = id.Identifier.Text;
                }
                else if (inv.Expression is MemberAccessExpressionSyntax ma && ma.Expression is ThisExpressionSyntax)
                {
                    name = ma.Name.Identifier.Text;
                }

                if (name != null && knownMethodNames.Contains(name))
                    found.Add(name);
            }

            return found;
        }
    }
}
