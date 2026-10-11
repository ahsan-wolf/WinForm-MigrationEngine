namespace Migrator.Engine.Projections;

/// <summary>
/// project.ir.json: index over the per-unit IR files. An additive wrapper owned by this repo; the shared PageIr is unchanged
/// and consumers may ignore this file.
/// </summary>
public sealed class ProjectIr
{
    public string SchemaVersion { get; set; } = "";
    public ProjectSourceIr Source { get; set; } = new();
    public List<ProjectUnitIr> Forms { get; set; } = new();
    public List<ProjectUnitIr> UserControls { get; set; } = new();
    public ProjectSharedIr Shared { get; set; } = new();

    /// <summary>Units whose extraction failed. Listed rather than silently omitted (failure isolation must stay visible).</summary>
    public List<ProjectFailureIr> Failures { get; set; } = new();
}

public sealed class ProjectSourceIr
{
    public string Technology { get; set; } = "";
    public string? FrameworkVersion { get; set; }
    public string RootPath { get; set; } = "";
    public string? StartupForm { get; set; }
}

/// <summary><c>IrFile</c> is relative to the folder holding project.ir.json, with forward slashes.</summary>
public sealed class ProjectUnitIr
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string IrFile { get; set; } = "";
}

/// <summary>Not populated yet (empty lists); shape is fixed so consumers can rely on it.</summary>
public sealed class ProjectSharedIr
{
    public List<string> Settings { get; set; } = new();
    public List<string> Resources { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public List<string> Unsupported { get; set; } = new();
}

public sealed class ProjectFailureIr
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Error { get; set; } = "";
}
