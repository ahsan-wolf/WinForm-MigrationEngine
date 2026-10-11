using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Facet "workflows": for every control.Event -> handler wiring, the handler's direct calls to other methods of the
/// same class become ordered steps. Steps follow <b>call order</b> in the handler body (first occurrence), not declaration
/// order. Only direct calls are followed (not 2+ levels deep) — known limitation.
/// </summary>
public sealed class WinFormsWorkflowExtractor : IIrExtractor
{
    public string Facet => "workflows";

    public void Extract(IrExtractionContext context, PageIr target)
    {
        var methodNames = target.Members.Where(m => m.Kind == MemberKind.Method).Select(m => m.Name).ToHashSet();

        foreach (var wiring in EventWiringReader.Read(target))
        {
            var steps = CalledOwnMethods(wiring.Handler, methodNames)
                .Select(name => new WorkflowStepIr { MethodName = name, Semantic = SemanticKeywords.Infer(name) })
                .ToList();

            target.Workflows.Add(new WorkflowIr
            {
                Name = $"{wiring.Control.Id}.{wiring.EventName}",
                TriggerControlId = wiring.Control.Id,
                TriggerEvent = wiring.EventName,
                HandlerMethod = wiring.Handler.Name,
                Steps = steps,
                Semantic = wiring.Control.Semantic ?? FallbackSemantic(steps)
            });
        }
    }

    private static List<string> CalledOwnMethods(MemberIr handler, HashSet<string> knownMethods)
    {
        var called = new List<string>();
        foreach (var inv in EventWiringReader.ParseBody(handler.Body).DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = inv.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } ma => ma.Name.Identifier.Text,
                _ => null
            };
            if (name != null && name != handler.Name && knownMethods.Contains(name) && !called.Contains(name))
                called.Add(name);
        }
        return called;
    }

    private static string FallbackSemantic(List<WorkflowStepIr> steps) => steps.Count switch
    {
        0 => "No delegated business logic detected",
        1 => steps[0].Semantic ?? $"Calls {steps[0].MethodName}",
        _ => $"Runs {steps.Count} step(s): {string.Join(", ", steps.Select(s => s.MethodName))}"
    };
}
