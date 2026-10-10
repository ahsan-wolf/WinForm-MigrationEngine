using WebFormsMigrator.IR.Models;

namespace WebFormsMigrator.IR.Contracts;

// PROVISIONAL. The real IIrExtractor / IIrGenerator signatures are not available to this repo;
// these follow the roles described in reference/PROJECT_DOCUMENTATION.md (§2.3, §3.2).
// Reconcile with the shared package when it is wired in (build/IrReference.props).

/// <summary>Raw input for one page/control. For WinForms: Markup = Designer.cs text, CodeBehind = code-behind text.</summary>
public sealed class IrExtractionContext
{
    public string FilePath { get; init; } = "";
    public string Markup { get; init; } = "";
    public string CodeBehind { get; init; } = "";
}

/// <summary>Contributes one facet of a <see cref="PageIr"/>.</summary>
public interface IIrExtractor
{
    string Name { get; }
    void Extract(IrExtractionContext context, PageIr page);
}

public sealed record GeneratedFile(string RelativePath, string Content);

/// <summary>Consumes a finished <see cref="PageIr"/> and produces files for one target.</summary>
public interface IIrGenerator
{
    string Target { get; }
    IReadOnlyList<GeneratedFile> Generate(PageIr page);
}
