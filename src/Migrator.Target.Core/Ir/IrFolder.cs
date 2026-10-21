using System.Text.Json;
using Migrator.Engine.Projections;
using Migrator.Engine.Support;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.Core.Ir;

public sealed record UnitIr(string Name, string Kind, PageIr Page, IReadOnlyDictionary<string, ControlLayout> Layout)
{
    public bool IsUserControl => Kind == "UserControl";
    public string Title => Page.RootControl?.Attributes.GetValueOrDefault("Text") is { Length: > 0 } t ? t : Name;
}

/// <summary>The IR folder written by the ir step: project.ir.json plus each unit's complete and layout files. The only input a target reads.</summary>
public sealed class IrFolder
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ProjectIr Project { get; private init; } = new();

    /// <summary>The project's own non-UI classes (support.ir.json), when the source engine extracted any.</summary>
    public SupportFileIr? Support { get; private init; }

    /// <summary>Images of the project's resource file that its code references (resources.ir.json), when the source engine extracted any.</summary>
    public ResourceFileIr? Resources { get; private init; }
    public IReadOnlyDictionary<string, UnitIr> Units { get; private init; } = new Dictionary<string, UnitIr>();

    public static IrFolder Load(string irFolder)
    {
        var projectPath = Path.Combine(irFolder, "project.ir.json");
        if (!File.Exists(projectPath)) throw new FileNotFoundException($"project.ir.json not found in '{irFolder}'. Run the ir step first.");

        var project = JsonSerializer.Deserialize<ProjectIr>(File.ReadAllText(projectPath), Json)
                      ?? throw new InvalidDataException("project.ir.json is empty.");

        var units = new Dictionary<string, UnitIr>(StringComparer.Ordinal);
        foreach (var entry in project.Forms.Concat(project.UserControls))
        {
            var completePath = Path.Combine(irFolder, entry.IrFile.Replace('/', Path.DirectorySeparatorChar));
            var page = JsonSerializer.Deserialize<PageIr>(File.ReadAllText(completePath), Json)
                       ?? throw new InvalidDataException($"{entry.IrFile} is empty.");

            var layout = new Dictionary<string, ControlLayout>(StringComparer.Ordinal);
            var layoutPath = completePath.Replace(".complete.ir.json", ".layout.ir.json");
            if (File.Exists(layoutPath) && JsonSerializer.Deserialize<LayoutFile>(File.ReadAllText(layoutPath), Json) is { } file)
                foreach (var c in file.Controls) layout[c.Id] = c;

            units[entry.Name] = new UnitIr(entry.Name, entry.Kind, page, layout);
        }
        var supportPath = Path.Combine(irFolder, "support.ir.json");
        var support = File.Exists(supportPath) ? JsonSerializer.Deserialize<SupportFileIr>(File.ReadAllText(supportPath), Json) : null;
        var resourcesPath = Path.Combine(irFolder, "resources.ir.json");
        var resources = File.Exists(resourcesPath) ? JsonSerializer.Deserialize<ResourceFileIr>(File.ReadAllText(resourcesPath), Json) : null;
        return new IrFolder { Project = project, Units = units, Support = support, Resources = resources };
    }
}
