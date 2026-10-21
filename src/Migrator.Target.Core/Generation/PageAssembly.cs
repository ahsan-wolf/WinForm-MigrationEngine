using Migrator.Engine.Contracts;
using Migrator.Target.Core.Translation;

namespace Migrator.Target.Core.Generation;

/// <summary>One line of a page's `wiring` table: which handler a control event runs and where the subscription came from.</summary>
public sealed record WiringEntry(string Control, string Event, string Handler, string Method, string Source);

/// <summary>One control's entry of the page model (`vm.&lt;id&gt;`): property key -> TypeScript initial value (`@raw:` values are literal member text).</summary>
public sealed record ModelEntry(string Id, IReadOnlyList<KeyValuePair<string, string>> Props);

/// <summary>
/// Everything a framework needs to turn one page into files. The generator decides the markup (as JSX-like lines), the model, the handlers and the
/// imports it needs; an <see cref="IPageAssembler"/> decides how that becomes files. Without an assembler the generator writes its own React pair of files.
/// </summary>
public sealed class PageAssemblyInput
{
    public string UnitName { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Component { get; init; } = "";

    /// <summary>The markup as JSX-like lines (the generator's neutral view form): `&lt;WfButton text="Save" onClick={handler} /&gt;`.</summary>
    public IReadOnlyList<string> View { get; init; } = Array.Empty<string>();

    /// <summary>Handler and translated functions, declared as `function name() { ... }` at column 0.</summary>
    public IReadOnlyList<string> HandlerCode { get; init; } = Array.Empty<string>();

    /// <summary>Top-level declarations of the code-behind (`const xColumns: WfGridColumn[] = [...]`).</summary>
    public IReadOnlyList<string> TopLevel { get; init; } = Array.Empty<string>();

    public IReadOnlyList<CtorProp> CtorProps { get; init; } = Array.Empty<CtorProp>();

    /// <summary>The constructor-argument props the field model reads (only these are bound in the code-behind).</summary>
    public IReadOnlyList<CtorProp> UsedProps { get; init; } = Array.Empty<CtorProp>();
    public IReadOnlyList<string> CtorNotes { get; init; } = Array.Empty<string>();

    public bool UsesDialog { get; init; }
    public bool UsesModel { get; init; }
    public bool UsesFields { get; init; }

    public IReadOnlyList<ModelEntry> Model { get; init; } = Array.Empty<ModelEntry>();
    public IReadOnlyList<string> ModelComments { get; init; } = Array.Empty<string>();
    public IReadOnlyList<KeyValuePair<string, string>> FieldInitials { get; init; } = Array.Empty<KeyValuePair<string, string>>();
    public IReadOnlyList<WiringEntry> Wiring { get; init; } = Array.Empty<WiringEntry>();

    /// <summary>Library names the markup and code use (components, helpers) and library types.</summary>
    public IReadOnlyCollection<string> Lib { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> LibTypes { get; init; } = Array.Empty<string>();

    /// <summary>Other generated pages this page opens or hosts: component -> component.</summary>
    public IReadOnlyCollection<string> PageImports { get; init; } = Array.Empty<string>();

    /// <summary>Contract types the code mentions (already filtered to those used).</summary>
    public IReadOnlyList<string> TypeImports { get; init; } = Array.Empty<string>();
    public string? ServiceImport { get; init; }
    public string ServiceInstance { get; init; } = "";
    public string TypesImportPath { get; init; } = "";
    public string FixturesImportPath { get; init; } = "";
    public bool NeedsFixtures { get; init; }
    public IReadOnlyCollection<string> SupportClasses { get; init; } = Array.Empty<string>();

    /// <summary>The assembler adds its own issues here (a control the framework has no component for).</summary>
    public List<GenerationIssue> Issues { get; } = new();
}

/// <summary>The files of one page, as a target framework lays them out.</summary>
public sealed record PageFiles(string View, string Code, string Component);

/// <summary>A target framework's way of writing a page: markup file, code-behind file and, where the framework wants one, a component file.</summary>
public interface IPageAssembler
{
    PageFiles Assemble(PageAssemblyInput input);
}
