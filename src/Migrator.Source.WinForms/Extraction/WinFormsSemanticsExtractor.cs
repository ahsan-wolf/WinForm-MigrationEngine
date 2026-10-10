using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>Facet "semantics": labels every control from <see cref="SemanticKeywords"/> (Id first, then Text; first match wins).</summary>
public sealed class WinFormsSemanticsExtractor : IIrExtractor
{
    public string Facet => "semantics";

    public void Extract(IrExtractionContext context, PageIr target)
    {
        if (target.RootControl == null) return;
        foreach (var child in target.RootControl.Children) Label(child);
    }

    private static void Label(ControlNodeIr node)
    {
        node.Attributes.TryGetValue("Text", out var text);
        node.Semantic = SemanticKeywords.Infer(node.Id, text);
        foreach (var child in node.Children) Label(child);
    }
}
