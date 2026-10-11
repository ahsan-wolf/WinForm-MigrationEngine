using Migrator.Engine.Navigation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Facet "navigation": finds <c>new X().Show()/ShowDialog()</c>, <c>this.Close()/Hide()</c> (own form only) and <c>Application.Exit()</c> and records
/// them through <see cref="WinNavigation"/> (the shared NavigationIr is not changed).
/// <list type="bullet">
/// <item>Sites are found in <b>every</b> method and attributed to each control event that can reach them through calls to the
/// form's own methods (transitive, cycle-safe), because real code usually opens forms from helpers a handler calls.</item>
/// <item>A site no wired handler can reach is kept (never dropped) against the root control with
/// <c>TriggerEvent = "Method:{name}"</c>.</item>
/// <item><c>Show</c>/<c>ShowDialog</c> count only on <c>new T()</c> or a local initialised with one, and never for
/// <see cref="NonFormTypes"/> (file/color dialogs and message boxes are not navigation).</item>
/// </list>
/// </summary>
public sealed class WinFormsNavigationExtractor : IIrExtractor
{
    private const int MaxCallDepth = 8;

    /// <summary>Common dialogs: modal UI, but not another form of the application.</summary>
    private static readonly HashSet<string> NonFormTypes = new()
    {
        "OpenFileDialog", "SaveFileDialog", "FolderBrowserDialog", "ColorDialog", "FontDialog",
        "PrintDialog", "PageSetupDialog", "PrintPreviewDialog", "MessageBox"
    };

    private sealed record Site(WinNavigationKind Kind, string? Form);

    public string Facet => "navigation";

    public void Extract(IrExtractionContext context, PageIr target)
    {
        var methods = target.Members.Where(m => m.Kind == MemberKind.Method && m.Body != null).ToList();
        var names = methods.Select(m => m.Name).ToHashSet();

        // Per method name (overloads merged): own-method calls and navigation sites.
        var calls = new Dictionary<string, HashSet<string>>();
        var sites = new Dictionary<string, List<Site>>();
        foreach (var method in methods)
        {
            var body = EventWiringReader.ParseBody(method.Body);
            if (!calls.TryGetValue(method.Name, out var called)) calls[method.Name] = called = new HashSet<string>();
            foreach (var inv in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (OwnCallName(inv) is { } callee && callee != method.Name && names.Contains(callee)) called.Add(callee);
            }

            if (!sites.TryGetValue(method.Name, out var list)) sites[method.Name] = list = new List<Site>();
            list.AddRange(FindSites(body));
        }

        var seen = new HashSet<(string, string, WinNavigationKind, string)>();
        var reached = new HashSet<string>();

        foreach (var wiring in EventWiringReader.Read(target))
        {
            foreach (var name in Reach(wiring.Handler.Name, calls))
            {
                reached.Add(name);
                foreach (var site in sites.GetValueOrDefault(name) ?? new List<Site>())
                    Add(target, seen, wiring.Control.Id, wiring.EventName, site);
            }
        }

        // Sites in methods no wired handler reaches: keep them, attributed to the form itself.
        var rootId = target.RootControl?.Id ?? target.Name;
        foreach (var (name, list) in sites)
        {
            if (reached.Contains(name)) continue;
            foreach (var site in list) Add(target, seen, rootId, $"Method:{name}", site);
        }
    }

    private static void Add(PageIr page, HashSet<(string, string, WinNavigationKind, string)> seen, string controlId, string eventName, Site site)
    {
        if (seen.Add((controlId, eventName, site.Kind, site.Form ?? "")))
            page.Navigations.Add(WinNavigation.Create(controlId, eventName, site.Kind, site.Form));
    }

    /// <summary>Method names reachable from <paramref name="start"/> (inclusive), breadth-first, bounded and cycle-safe.</summary>
    private static List<string> Reach(string start, Dictionary<string, HashSet<string>> calls)
    {
        var order = new List<string> { start };
        var visited = new HashSet<string> { start };
        var frontier = new List<string> { start };
        for (var depth = 0; depth < MaxCallDepth && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var name in frontier)
                foreach (var callee in calls.GetValueOrDefault(name) ?? new HashSet<string>())
                    if (visited.Add(callee)) { order.Add(callee); next.Add(callee); }
            frontier = next;
        }
        return order;
    }

    private static string? OwnCallName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } ma => ma.Name.Identifier.Text,
        _ => null
    };

    private static List<Site> FindSites(SyntaxNode body)
    {
        var localTypes = body.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Initializer?.Value is ObjectCreationExpressionSyntax)
            .GroupBy(v => v.Identifier.Text)
            .ToDictionary(g => g.Key, g => ((ObjectCreationExpressionSyntax)g.First().Initializer!.Value).Type.ToString());

        var found = new List<Site>();
        foreach (var inv in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            if (Classify(inv, localTypes) is { } site) found.Add(site);
        return found;
    }

    private static Site? Classify(InvocationExpressionSyntax inv, Dictionary<string, string> localTypes)
    {
        // Bare Close(); / Hide(); inside the form's own code.
        if (inv.Expression is IdentifierNameSyntax { Identifier.Text: "Close" or "Hide" } bare)
            return new Site(Enum.Parse<WinNavigationKind>(bare.Identifier.Text), null);

        if (inv.Expression is not MemberAccessExpressionSyntax ma) return null;
        var method = ma.Name.Identifier.Text;
        var receiver = ma.Expression;

        if (method == "Exit" && receiver.ToString() == "Application") return new Site(WinNavigationKind.Exit, null);

        switch (method)
        {
            case "Close" or "Hide":
                // Only the form closing/hiding itself. conn.Close(), reader.Close(), stream.Close() are everywhere in
                // real code, and a receiver's type is not knowable from syntax alone, so other receivers are not navigation.
                return receiver is ThisExpressionSyntax ? new Site(Enum.Parse<WinNavigationKind>(method), null) : null;

            case "Show" or "ShowDialog":
                var form = ResolveForm(receiver, localTypes, requireCreation: true);
                return form == null || IsNonForm(form) ? null : new Site(Enum.Parse<WinNavigationKind>(method), form);

            default:
                return null;
        }
    }

    private static bool IsNonForm(string typeName) => NonFormTypes.Contains(typeName[(typeName.LastIndexOf('.') + 1)..]);

    private static string? ResolveForm(ExpressionSyntax receiver, Dictionary<string, string> localTypes, bool requireCreation = false)
    {
        if (receiver is ObjectCreationExpressionSyntax oce) return oce.Type.ToString();
        if (receiver is IdentifierNameSyntax id && localTypes.TryGetValue(id.Identifier.Text, out var type)) return type;
        return requireCreation ? null : receiver.ToString();
    }
}
