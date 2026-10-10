using Migrator.Engine.Hosting;

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine("""
            migrator <command> [options]

            Commands:
              engines [--config <engines.json>]   List the engines that load from engines.json

            (migrate / ir arrive in later phases.)
            """);
        return args.Length == 0 ? 1 : 0;
    }

    return args[0] switch
    {
        "engines" => ListEngines(args[1..]),
        _ => Fail($"Unknown command '{args[0]}'. Try 'migrator --help'.")
    };
}

static int ListEngines(string[] args)
{
    var configPath = OptionValue(args, "--config") ?? FindEnginesJson();
    if (configPath is null || !File.Exists(configPath))
        return Fail("engines.json not found. Pass --config <path> or run from inside the repo.");

    var result = EngineLoader.Load(configPath);

    Console.WriteLine($"Config: {Path.GetFullPath(configPath)}   Host IR schema: {result.Registry.HostSchemaVersion}");
    Console.WriteLine();
    foreach (var s in result.Registry.Sources)
        Console.WriteLine($"  source  {s.Id,-16} {s.Technology,-10} v{s.Version}  schema {s.SchemaVersion}  extractors: {s.Extractors.Count()}");
    foreach (var t in result.Registry.Targets)
        Console.WriteLine($"  target  {t.Id,-16} ui: {t.UiLibraryPackage,-14} v{t.Version}  schema {t.SchemaVersion}");
    if (!result.Registry.Sources.Any() && !result.Registry.Targets.Any())
        Console.WriteLine("  (no engines loaded)");

    if (result.Diagnostics.Count > 0)
    {
        Console.WriteLine();
        foreach (var d in result.Diagnostics)
            Console.WriteLine($"  [{d.Severity.ToString().ToUpperInvariant()}] {d.Message}");
    }

    return result.HasErrors ? 2 : 0;
}

static string? OptionValue(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// Walk up from the working directory so `dotnet run --project src/Migrator.Cli` works from anywhere in the repo.
static string? FindEnginesJson()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "engines.json");
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
