using System.Text.Json;

namespace Migrator.Target.React.Spec;

public sealed record SpecProp(string Name, string Type, string[] From, string? Transform);
public sealed record SpecEvent(string Name, string From, string React);

/// <summary>A library component to render instead of a WinForms control that has none: property/event aliases rename WinForms members to the ones that component reads.</summary>
public sealed record SpecFallback(string Use, IReadOnlyDictionary<string, string> PropertyAliases, IReadOnlyDictionary<string, string> EventAliases, string? Note);

public sealed record SpecControl(string Name, string[] WinformsTypes, bool Container, string[] Slots, SpecProp[] Props, SpecEvent[] Events);

/// <summary>ui-spec/controls.json read as data: which library component replaces which WinForms type, and how properties/events map.</summary>
public sealed class SpecCatalog
{
    private readonly Dictionary<string, SpecControl> _byType = new(StringComparer.Ordinal);

    public string SpecVersion { get; private init; } = "";
    public IReadOnlyList<SpecControl> Controls { get; private init; } = Array.Empty<SpecControl>();

    private readonly Dictionary<string, SpecFallback> _fallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpecControl> _byName = new(StringComparer.Ordinal);

    /// <summary>Component used for an unknown control with children (default: a panel).</summary>
    public string DefaultWithChildren { get; private set; } = "WfPanel";

    /// <summary>Component used for any other unknown control (default: a label).</summary>
    public string DefaultOther { get; private set; } = "WfLabel";

    public SpecControl? Find(string winformsType) => _byType.GetValueOrDefault(winformsType);

    public SpecControl? ByName(string componentName) => _byName.GetValueOrDefault(componentName);

    public SpecFallback? FindFallback(string winformsType) => _fallbacks.GetValueOrDefault(winformsType);

    public static SpecCatalog Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"ui-spec controls.json not found: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var controls = root.GetProperty("controls").EnumerateArray().Select(ReadControl).ToList();

        var catalog = new SpecCatalog { SpecVersion = root.GetProperty("specVersion").GetString() ?? "", Controls = controls };
        foreach (var control in controls)
        {
            catalog._byName[control.Name] = control;
            foreach (var type in control.WinformsTypes)
                catalog._byType[type] = control;
        }
        catalog.LoadFallbacks(Path.Combine(Path.GetDirectoryName(path) ?? "", "fallbacks.json"));
        return catalog;
    }

    private void LoadFallbacks(string path)
    {
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        static Dictionary<string, string> Map(JsonElement e, string name) =>
            e.TryGetProperty(name, out var obj) && obj.ValueKind == JsonValueKind.Object
                ? obj.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
                : new Dictionary<string, string>();

        if (doc.RootElement.TryGetProperty("fallbacks", out var list))
            foreach (var f in list.EnumerateArray())
            {
                var fallback = new SpecFallback(f.GetProperty("use").GetString()!, Map(f, "propertyAliases"), Map(f, "eventAliases"),
                    f.TryGetProperty("note", out var n) ? n.GetString() : null);
                foreach (var type in Strings(f, "winformsTypes")) _fallbacks[type] = fallback;
            }
        if (doc.RootElement.TryGetProperty("default", out var def))
        {
            if (def.TryGetProperty("withChildren", out var wc)) DefaultWithChildren = wc.GetString() ?? DefaultWithChildren;
            if (def.TryGetProperty("other", out var other)) DefaultOther = other.GetString() ?? DefaultOther;
        }
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
