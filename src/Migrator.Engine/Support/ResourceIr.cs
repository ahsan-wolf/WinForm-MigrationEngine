using WebFormsMigrator.IR.Models;

namespace Migrator.Engine.Support;

/// <summary>
/// Images of the project's own resource file (<c>Properties/Resources.resx</c>, reached from code as <c>Properties.Resources.Name</c>) that the code references.
/// A WinForms-owned sidecar of the shared IR (<c>ir/resources.ir.json</c>), like <see cref="SupportFileIr"/>: only referenced images are kept, so the file stays small.
/// </summary>
public sealed class ResourceFileIr
{
    public string SchemaVersion { get; set; } = IrSchema.Version;
    public List<ResourceImageIr> Images { get; set; } = new();
}

public sealed class ResourceImageIr
{
    /// <summary>The resource name, as in <c>Properties.Resources.Name</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>png, gif, jpg, bmp, ico or svg (from the file signature).</summary>
    public string Format { get; set; } = "";

    /// <summary>The file bytes, base64.</summary>
    public string Data { get; set; } = "";
}
