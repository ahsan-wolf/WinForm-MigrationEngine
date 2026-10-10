using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Models;

namespace WebFormsMigrator.WinFormsAnalyzers
{
    /// <summary>
    /// WinForms has no NavigateUrl/PostBackUrl/Response.Redirect equivalent, but it
    /// has an analogous set of "this changes what's on screen" calls: opening
    /// another Form (Show/ShowDialog), closing this one (Close/Hide), or exiting
    /// the app (Application.Exit). This extractor looks for those inside handler
    /// bodies, the WinForms counterpart of NavigationWorkflowExtractor's
    /// NavigationIr extraction.
    /// </summary>
    public static class WinFormsNavigationExtractor
    {
        private static readonly HashSet<string> FormNavigationMethods = new() { "Show", "ShowDialog", "Close", "Hide" };

        public static List<NavigationIr> Extract(DesignerParseResult designer, string codeBehindSourceText)
        {
            var navigations = new List<NavigationIr>();
            var tree = CSharpSyntaxTree.ParseText(codeBehindSourceText);
            var root = tree.GetCompilationUnitRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (method.Body == null) continue;

                var (controlId, eventName) = FindTriggerFor(designer, method.Identifier.Text);
                if (controlId == null) continue;

                foreach (var inv in method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (inv.Expression is not MemberAccessExpressionSyntax ma) continue;
                    var calledMethodName = ma.Name.Identifier.Text;
                    var targetText = ma.Expression.ToString();

                    if (targetText == "Application" && calledMethodName == "Exit")
                    {
                        navigations.Add(new NavigationIr
                        {
                            TriggerControlId = controlId,
                            TriggerEvent = eventName!,
                            HandlerMethod = method.Identifier.Text,
                            TargetForm = "Application",
                            Kind = "Exit"
                        });
                    }
                    else if (FormNavigationMethods.Contains(calledMethodName))
                    {
                        var targetForm = ma.Expression is ObjectCreationExpressionSyntax oce
                            ? oce.Type.ToString()
                            : targetText; // e.g. "this" for Close()/Hide() on the current form

                        navigations.Add(new NavigationIr
                        {
                            TriggerControlId = controlId,
                            TriggerEvent = eventName!,
                            HandlerMethod = method.Identifier.Text,
                            TargetForm = targetForm,
                            Kind = calledMethodName
                        });
                    }
                }
            }

            return navigations;
        }

        private static (string? ControlId, string? EventName) FindTriggerFor(DesignerParseResult designer, string handlerMethod)
        {
            foreach (var controlEntry in designer.ControlEventHandlers)
            {
                foreach (var handlerEntry in controlEntry.Value)
                {
                    if (handlerEntry.Value == handlerMethod)
                        return (controlEntry.Key, handlerEntry.Key);
                }
            }
            return (null, null);
        }
    }
}
