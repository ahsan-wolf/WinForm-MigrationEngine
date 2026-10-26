using System.Text.Json;
using System.Text.RegularExpressions;

namespace Migrator.Target.Core.Spec;

public sealed record SpecProp(string Name, string Type, string[] From, string? Transform);
public sealed record SpecEvent(string Name, string From, string React);

/// <summary>A library component to render instead of a WinForms control that has none: property/event aliases rename WinForms members to the ones that component reads.</summary>
public sealed record SpecFallback(string Use, IReadOnlyDictionary<string, string> PropertyAliases, IReadOnlyDictionary<string, string> EventAliases, string? Note, IReadOnlyDictionary<string, string>? Attributes = null);

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

    private readonly List<(Regex Pattern, SpecFallback Fallback)> _patternFallbacks = new();
    private readonly List<Regex> _nonVisualPatterns = new();

    /// <summary>The fallback for a type: an exact entry, else (third-party and custom types only, never `System.*`) the first name pattern that matches its simple name.</summary>
    public SpecFallback? FindFallback(string winformsType)
    {
        if (_fallbacks.TryGetValue(winformsType, out var exact)) return exact;
        if (winformsType.StartsWith("System.", StringComparison.Ordinal)) return null;
        var simple = SimpleName(winformsType);
        foreach (var (pattern, fallback) in _patternFallbacks)
            if (pattern.IsMatch(simple)) return fallback;
        return null;
    }

    /// <summary>A third-party or custom type that is a helper without a visual of its own (a provider, a controller, a generated report class): listed, never rendered.</summary>
    public bool IsNonVisual(string winformsType) =>
        !winformsType.StartsWith("System.", StringComparison.Ordinal) && _nonVisualPatterns.Any(p => p.IsMatch(SimpleName(winformsType)));

    private static string SimpleName(string type) => type[(type.LastIndexOf('.') + 1)..];

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
                    f.TryGetProperty("note", out var n) ? n.GetString() : null, Map(f, "attributes"));
                foreach (var type in Strings(f, "winformsTypes")) _fallbacks[type] = fallback;
            }
        if (doc.RootElement.TryGetProperty("namePatterns", out var patterns))
            foreach (var p in patterns.EnumerateArray())
                _patternFallbacks.Add((new Regex(p.GetProperty("pattern").GetString()!, RegexOptions.Compiled), new SpecFallback(p.GetProperty("use").GetString()!,
                    Map(p, "propertyAliases"), Map(p, "eventAliases"), p.TryGetProperty("note", out var pn) ? pn.GetString() : null, Map(p, "attributes"))));
        if (doc.RootElement.TryGetProperty("nonVisualPatterns", out var nonVisual))
            foreach (var p in nonVisual.EnumerateArray()) _nonVisualPatterns.Add(new Regex(p.GetString()!, RegexOptions.Compiled));
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
