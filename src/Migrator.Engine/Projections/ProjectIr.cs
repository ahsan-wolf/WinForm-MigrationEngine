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

    /// <summary>Referenced project folders outside <see cref="RootPath"/> whose classes were included.</summary>
    public List<string> AdditionalRoots { get; set; } = new();
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

    /// <summary>Binary assemblies the project references (not framework, not project references): candidates for contract extraction.</summary>
    public List<ExternalAssemblyIr> ExternalAssemblies { get; set; } = new();
}

/// <summary>A referenced assembly: its name, the path its project resolves it to (HintPath relative to the project, made absolute), and whether that file exists.</summary>
public sealed class ExternalAssemblyIr
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public bool Exists { get; set; }

    /// <summary>The project file(s) that reference it.</summary>
    public List<string> ReferencedBy { get; set; } = new();
}

public sealed class ProjectFailureIr
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Error { get; set; } = "";
}
