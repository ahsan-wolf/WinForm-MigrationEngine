namespace Migrator.Engine.Contracts;

/// <summary>What a project-level generation run should produce.</summary>
public sealed class GenerationOptions
{
    /// <summary>Form to start from; defaults to the project's startup form.</summary>
    public string? EntryForm { get; init; }

    /// <summary>How many navigation hops from the entry form to generate (0 = entry only, 1 = entry + forms it opens, ...). Null = everything reachable.</summary>
    public int? Depth { get; init; } = 1;

    /// <summary>
    /// Component viewer: a separate app (`<out>/<target>-viewer`) with a visual-only page for every form and user control (no handlers, data, translation or
    /// services) and a shell that lists them and loads the selected one. Implies all units; entry form, depth, contracts and translation are ignored.
    /// </summary>
    public bool Preview { get; init; }

    /// <summary>
    /// WinForms-style app shell (`<out>/<target>-appshell`): the same visual-only pages as <see cref="Preview"/>, hosted by a bordered window with hyperlink navigation on the left
    /// and a form-style frame on the right. Implies <see cref="Preview"/>.
    /// </summary>
    public bool AppShell { get; init; }

    /// <summary>Generate every form and user control of the project, not only those reachable from the entry form by Show/ShowDialog (needed for shells that open modules dynamically).</summary>
    public bool AllUnits { get; init; }

    /// <summary>
    /// Generate a `/modules` catalog page: every unit that code never opens (loaded dynamically or by permission logic in the original) listed by archetype
    /// (grid, tree, menu, toolbar, list, form) and opened in a content area. Implies generating those units, like <see cref="AllUnits"/>; on by default with it.
    /// </summary>
    public bool Catalog { get; init; }

    /// <summary>Name patterns that decide a unit's archetype when its designer cannot (data file, `rules/control-archetypes.json`); null = the target's default.</summary>
    public string? ArchetypeRulesPath { get; init; }

    /// <summary>contract.json files (from `migrator contract`) of binary-only dependencies the app calls; the target emits typed mock services from them.</summary>
    public IReadOnlyList<string> ContractFiles { get; init; } = Array.Empty<string>();

    /// <summary>Folder of image files named by key (`Name.png`, `Name.ico`, ...): supplies `Properties.Resources.Name` images and image-list entries that could not be extracted.</summary>
    public string? ImagesFolder { get; init; }

    /// <summary>"used" (default): only types/methods the generated forms reference. "all": every public data type and static method.</summary>
    public string ContractScope { get; init; } = "used";

    /// <summary>"none" (default: handlers stay commented C#), "rules" (deterministic C# -> TS) or "llm" (rules, then a local model for the rest; opt-in).</summary>
    public string Translate { get; init; } = "none";

    /// <summary>Run the TypeScript compiler on the generated app and demote translated methods that fail it. Installs npm dependencies first if they are missing.</summary>
    public bool CompileGate { get; init; }

    /// <summary>Skip the compile gate even when dependencies are installed (analysis: shows the raw TypeScript errors of translated code).</summary>
    public bool NoCompileGate { get; init; }
}

public sealed record GenerationIssue(string Unit, string Category, string Message);

public sealed class GenerationResult
{
    public string OutputDirectory { get; init; } = "";
    public List<string> GeneratedUnits { get; } = new();
    public List<string> SkippedUnits { get; } = new();
    public List<string> Files { get; } = new();
    public List<GenerationIssue> Issues { get; } = new();

    /// <summary>Named counters for the console summary (e.g. "methods.complete").</summary>
    public Dictionary<string, int> Stats { get; } = new();
}

/// <summary>
/// Optional: a target that generates a whole app from the IR **folder** written by the ir step (project.ir.json + per-unit files),
/// so it depends on IR files only and never on a source engine. The page-level <see cref="ITargetEngine.Generator"/> stays available.
/// </summary>
public interface IProjectTargetEngine : ITargetEngine
{
    GenerationResult GenerateProject(string irFolder, string outputDirectory, GenerationOptions options);
}
