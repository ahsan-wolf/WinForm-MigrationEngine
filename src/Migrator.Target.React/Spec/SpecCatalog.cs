using System.Text.Json;

namespace Migrator.Target.React.Spec;

public sealed record SpecProp(string Name, string Type, string[] From, string? Transform);
public sealed record SpecEvent(string Name, string From, string React);

public sealed record SpecControl(string Name, string[] WinformsTypes, bool Container, string[] Slots, SpecProp[] Props, SpecEvent[] Events);

/// <summary>ui-spec/controls.json read as data: which library component replaces which WinForms type, and how properties/events map.</summary>
public sealed class SpecCatalog
{
    private readonly Dictionary<string, SpecControl> _byType = new(StringComparer.Ordinal);

    public string SpecVersion { get; private init; } = "";
    public IReadOnlyList<SpecControl> Controls { get; private init; } = Array.Empty<SpecControl>();

    public SpecControl? Find(string winformsType) => _byType.GetValueOrDefault(winformsType);

    public static SpecCatalog Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"ui-spec controls.json not found: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var controls = root.GetProperty("controls").EnumerateArray().Select(ReadControl).ToList();

        var catalog = new SpecCatalog { SpecVersion = root.GetProperty("specVersion").GetString() ?? "", Controls = controls };
        foreach (var control in controls)
            foreach (var type in control.WinformsTypes)
                catalog._byType[type] = control;
        return catalog;
    }

    private static SpecControl ReadControl(JsonElement e) => new(
        e.GetProperty("name").GetString()!,
        Strings(e, "winformsTypes"),
        e.TryGetProperty("container", out var c) && c.GetBoolean(),
        Strings(e, "slots"),
        e.TryGetProperty("props", out var props)
            ? props.EnumerateArray().Select(p => new SpecProp(
                p.GetProperty("name").GetString()!,
                p.TryGetProperty("type", out var t) ? t.GetString() ?? "string" : "string",
                Strings(p, "from"),
                p.TryGetProperty("transform", out var tr) ? tr.GetString() : null)).ToArray()
            : Array.Empty<SpecProp>(),
        e.TryGetProperty("events", out var events)
            ? events.EnumerateArray().Select(ev => new SpecEvent(
                ev.GetProperty("name").GetString()!, ev.GetProperty("from").GetString()!, ev.GetProperty("react").GetString()!)).ToArray()
            : Array.Empty<SpecEvent>());

    private static string[] Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(x => x.GetString()!).ToArray()
            : Array.Empty<string>();
}
