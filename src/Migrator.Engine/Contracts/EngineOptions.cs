using System.Text.Json;

namespace Migrator.Engine.Contracts;

/// <summary>
/// Per-engine configuration from the "options" object in engines.json. Engines hold no global
/// state: a module may declare a constructor taking this type and pass what it needs to its engines.
/// </summary>
public sealed class EngineOptions
{
    public static EngineOptions Empty { get; } = new(null);

    private readonly JsonElement? _raw;

    public EngineOptions(JsonElement? raw, string? baseDirectory = null)
    {
        _raw = raw;
        BaseDirectory = baseDirectory;
    }

    /// <summary>Directory of the engines.json that declared these options (relative paths in options are relative to it).</summary>
    public string? BaseDirectory { get; }

    /// <summary>Resolves a path from the options against <see cref="BaseDirectory"/>; absolute paths are returned unchanged.</summary>
    public string ResolvePath(string path) =>
        Path.IsPathRooted(path) || BaseDirectory is null ? path : Path.GetFullPath(Path.Combine(BaseDirectory, path));

    public T Bind<T>() where T : new()
    {
        if (_raw is not { ValueKind: JsonValueKind.Object } raw) return new T();
        return raw.Deserialize<T>(EngineJson.Options) ?? new T();
    }
}
