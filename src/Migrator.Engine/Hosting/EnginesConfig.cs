using System.Text.Json;
using Migrator.Engine.Contracts;

namespace Migrator.Engine.Hosting;

/// <summary>engines.json. Authoritative allow-list: nothing is loaded unless it is listed and enabled.</summary>
public sealed class EnginesConfig
{
    /// <summary>Relative to the engines.json directory.</summary>
    public string PluginsDirectory { get; set; } = "plugins";

    public List<EngineEntry> Engines { get; set; } = new();

    public static EnginesConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<EnginesConfig>(stream, EngineJson.Options)
               ?? throw new InvalidDataException($"'{path}' is empty.");
    }
}

public sealed class EngineEntry
{
    /// <summary>Id the entry is expected to register (checked after load).</summary>
    public string Id { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Assembly simple name. Built-in engines are resolved from the app directory first, then from the plugins directory.</summary>
    public string Assembly { get; set; } = "";

    /// <summary>Optional full type name of the IEngineModule when the assembly contains several.</summary>
    public string? Module { get; set; }

    public JsonElement? Options { get; set; }
}
