using System.Text.Json;
using System.Text.RegularExpressions;

namespace Migrator.Target.Core.Translation;

/// <summary>
/// Markers of code that belongs on the server (database, file system, configuration, network stacks), loaded from a data file
/// (<c>rules/server-bound.json</c>; extend per project without a rebuild). Structural and name-agnostic.
/// </summary>
public sealed class ServerBoundRules
{
    public static ServerBoundRules Empty { get; } = new();

    private Regex? _pattern;
    public double ClassThreshold { get; private set; } = 0.5;
    public bool IsEmpty => _pattern is null;

    public static ServerBoundRules Load(string? path)
    {
        if (path is null || !File.Exists(path)) return Empty;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var markers = doc.RootElement.TryGetProperty("markers", out var m) ? m.EnumerateArray().Select(x => x.GetString()!).Where(x => x.Length > 0).ToList() : new List<string>();
        var rules = new ServerBoundRules();
        if (doc.RootElement.TryGetProperty("classThreshold", out var t) && t.TryGetDouble(out var threshold)) rules.ClassThreshold = threshold;
        if (markers.Count > 0)
            rules._pattern = new Regex(@"(?<![\w])(?:" + string.Join("|", markers.Select(Regex.Escape)) + @")(?![\w])", RegexOptions.Compiled);
        return rules;
    }

    /// <summary>The first server-side marker the code uses, or null.</summary>
    public string? Match(string? code) => _pattern is null || string.IsNullOrEmpty(code) ? null : _pattern.Match(code) is { Success: true } hit ? hit.Value : null;
}
