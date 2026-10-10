using Migrator.Engine.Navigation;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Facet "navigation": inside wired handlers, finds <c>new X().Show()/ShowDialog()</c>, <c>this.Close()/Hide()</c> and
/// <c>Application.Exit()</c> and records them through <see cref="WinNavigation"/> (the shared NavigationIr is not changed).
/// <c>Show</c>/<c>ShowDialog</c> count only when the receiver is a <c>new T()</c> or a local initialised with one, so
/// <c>MessageBox.Show(...)</c> is never mistaken for navigation.
/// </summary>
public sealed class WinFormsNavigationExtractor : IIrExtractor
{
    public string Facet => "navigation";

    public void Extract(IrExtractionContext context, PageIr target)
    {
        foreach (var wiring in EventWiringReader.Read(target))
        {
            var body = EventWiringReader.ParseBody(wiring.Handler.Body);
            var localTypes = body.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer?.Value is ObjectCreationExpressionSyntax)
                .GroupBy(v => v.Identifier.Text)
                .ToDictionary(g => g.Key, g => ((ObjectCreationExpressionSyntax)g.First().Initializer!.Value).Type.ToString());

            foreach (var inv in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var nav = Classify(inv, localTypes, wiring);
                if (nav != null) target.Navigations.Add(nav);
            }
        }
    }

    private static NavigationIr? Classify(InvocationExpressionSyntax inv, Dictionary<string, string> localTypes, EventWiring wiring)
    {
        NavigationIr Make(WinNavigationKind kind, string? form = null) =>
            WinNavigation.Create(wiring.Control.Id, wiring.EventName, kind, form);

        // Bare Close(); / Hide(); inside the form's own handler.
        if (inv.Expression is IdentifierNameSyntax { Identifier.Text: "Close" or "Hide" } bare)
            return Make(Enum.Parse<WinNavigationKind>(bare.Identifier.Text));

        if (inv.Expression is not MemberAccessExpressionSyntax ma) return null;
        var method = ma.Name.Identifier.Text;
        var receiver = ma.Expression;

        if (method == "Exit" && receiver.ToString() == "Application") return Make(WinNavigationKind.Exit);

        switch (method)
        {
            case "Close" or "Hide":
                return Make(Enum.Parse<WinNavigationKind>(method), receiver is ThisExpressionSyntax ? null : ResolveForm(receiver, localTypes));

            case "Show" or "ShowDialog":
                var form = ResolveForm(receiver, localTypes, requireCreation: true);
                return form == null ? null : Make(Enum.Parse<WinNavigationKind>(method), form);

            default:
                return null;
        }
    }

    private static string? ResolveForm(ExpressionSyntax receiver, Dictionary<string, string> localTypes, bool requireCreation = false)
    {
        if (receiver is ObjectCreationExpressionSyntax oce) return oce.Type.ToString();
        if (receiver is IdentifierNameSyntax id && localTypes.TryGetValue(id.Identifier.Text, out var type)) return type;
        return requireCreation ? null : receiver.ToString();
    }
}
