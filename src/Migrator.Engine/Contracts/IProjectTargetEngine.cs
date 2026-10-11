namespace Migrator.Engine.Contracts;

/// <summary>What a project-level generation run should produce.</summary>
public sealed class GenerationOptions
{
    /// <summary>Form to start from; defaults to the project's startup form.</summary>
    public string? EntryForm { get; init; }

    /// <summary>How many navigation hops from the entry form to generate (0 = entry only, 1 = entry + forms it opens, ...). Null = everything reachable.</summary>
    public int? Depth { get; init; } = 1;

    /// <summary>contract.json files (from `migrator contract`) of binary-only dependencies the app calls; the target emits typed mock services from them.</summary>
    public IReadOnlyList<string> ContractFiles { get; init; } = Array.Empty<string>();

    /// <summary>"used" (default): only types/methods the generated forms reference. "all": every public data type and static method.</summary>
    public string ContractScope { get; init; } = "used";
}

public sealed record GenerationIssue(string Unit, string Category, string Message);

public sealed class GenerationResult
{
    public string OutputDirectory { get; init; } = "";
    public List<string> GeneratedUnits { get; } = new();
    public List<string> SkippedUnits { get; } = new();
    public List<string> Files { get; } = new();
    public List<GenerationIssue> Issues { get; } = new();
}

/// <summary>
/// Optional: a target that generates a whole app from the IR **folder** written by the ir step (project.ir.json + per-unit files),
/// so it depends on IR files only and never on a source engine. The page-level <see cref="ITargetEngine.Generator"/> stays available.
/// </summary>
public interface IProjectTargetEngine : ITargetEngine
{
    GenerationResult GenerateProject(string irFolder, string outputDirectory, GenerationOptions options);
}
