using Migrator.Engine.Contracts;
using Migrator.Target.Core.Ir;
using Migrator.Target.Core.Spec;
using Migrator.Target.Core.Translation;

namespace Migrator.Target.Core.Generation;

/// <summary>Generates one unit (form or user control) as a page of the target framework.</summary>
public interface IPageGenerator
{
    /// <param name="demoted">Methods (or `method#line` statements) the compile gate demoted back to commented C#.</param>
    GeneratedPage Generate(UnitIr unit, ISet<string>? demoted = null);
}

/// <summary>
/// What differs between target frameworks for the shared <see cref="ProjectGenerator"/>: how pages are generated and written, the app skeleton, and how
/// generated code is type-checked. Pages live in `src/pages`, support code in `src/support`, services in `src/services`, helpers in `src/lib` for every target.
/// </summary>
public interface ITargetBackend
{
    /// <summary>`react-app`, `angular-app`: the title of the report.</summary>
    string Key { get; }
    string UiSpecPath { get; }
    string UiLibraryPath { get; }

    /// <summary>The package generated code imports library types and helpers from (`@wfm/ui-react`, `@wfm/ui-angular`).</summary>
    string UiLibraryPackage { get; }
    string BypassRulesPath { get; }
    string ServerBoundRulesPath { get; }

    /// <summary>`rules/control-archetypes.json`: name patterns that classify user controls the designer cannot (data file).</summary>
    string ArchetypeRulesPath { get; }

    IPageGenerator CreatePageGenerator(SpecCatalog spec, IReadOnlyDictionary<string, UnitIr> units, IReadOnlyDictionary<string, string> components, ISet<string> generated, TranslationEnv? env);

    /// <summary>Writes the page's files (markup, code-behind, legacy copy, assets).</summary>
    void WritePage(string appDir, GeneratedPage page, GenerationResult result);

    /// <summary>Path (relative to the app folder, forward slashes) of the file that holds the page's translated methods: type-check errors in it are mapped back to methods.</summary>
    string CodeFilePath(GeneratedPage page);

    /// <summary>Package, build config, entry point, routes and base styles.</summary>
    /// <param name="api">The app calls the generated dotnet-api (`src/lib/api.ts` exists): messages from the server open as popups.</param>
    /// <param name="catalog">The units of the `/modules` catalog page (empty = no catalog page).</param>
    void WriteApp(string appDir, string appName, string entryComponent, string uiLibraryDir, List<string> files, string? favicon, IReadOnlyList<CatalogEntry> catalog, bool api);

    bool CanRunTypeCheck(string appDir);
    bool TryInstall(string appDir, out string error);

    /// <summary>Null when the checker cannot run; otherwise every error it reports.</summary>
    List<TscDiagnostic>? RunTypeCheck(string appDir);
}

/// <summary>One unit of the `/modules` catalog: its component, archetype, title and whether code (a Show / ShowDialog / `new` of it) opens it at all.</summary>
public sealed record CatalogEntry(string Unit, string Component, ControlArchetype Archetype, string Title, bool OpenedByCode, bool IsUserControl);
