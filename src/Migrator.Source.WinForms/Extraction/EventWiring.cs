using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>One control.Event -> handler wiring whose handler is a method of this page.</summary>
internal sealed record EventWiring(ControlNodeIr Control, string EventName, MemberIr Handler);

internal static class EventWiringReader
{
    /// <summary>
    /// Reads wiring back out of the control tree (<c>Attributes["OnClick"] = "btnSave_Click"</c>), so workflow and
    /// navigation derive from the IR alone. Wiring whose handler isn't a member of this page (third partial file,
    /// base class, or a handler inside an inlined UserControl) is skipped.
    /// </summary>
    public static IEnumerable<EventWiring> Read(PageIr page)
    {
        if (page.RootControl == null) yield break;

        var methods = page.Members.Where(m => m.Kind == MemberKind.Method).ToDictionary(m => m.Name, m => m);
        foreach (var node in Walk(page.RootControl))
        foreach (var (key, value) in node.Attributes)
        {
            if (key.Length > 2 && key.StartsWith("On", StringComparison.Ordinal) && char.IsUpper(key[2])
                && methods.TryGetValue(value, out var handler))
                yield return new EventWiring(node, key[2..], handler);
        }
    }

    private static IEnumerable<ControlNodeIr> Walk(ControlNodeIr node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var n in Walk(child))
            yield return n;
    }

    /// <summary>Re-parses a method body so its syntax can be inspected (bodies are stored as text in the IR).</summary>
    public static SyntaxNode ParseBody(string? body) =>
        CSharpSyntaxTree.ParseText("class __Wrapper { void __M() " + (body ?? "{ }") + " }").GetRoot();
}
