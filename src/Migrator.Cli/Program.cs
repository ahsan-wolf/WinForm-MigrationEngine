using System.Text.Json;
using Migrator.Engine.Hosting;
using Migrator.Engine.Pipeline;

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine("""
            migrator <command> [options]

            Commands:
              engines [--config <engines.json>]   List the engines that load from engines.json
              ir --source <dir> --out <dir> [--engine <id>] [--config <engines.json>]
                                                  Extract IR only (no generation); writes <out>/ir/<Form>/<Form>.complete.ir.json

            (migrate / ir arrive in later phases.)
            """);
        return args.Length == 0 ? 1 : 0;
    }

    return args[0] switch
    {
        "engines" => ListEngines(args[1..]),
        "ir" => ExtractIr(args[1..]),
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

static int ExtractIr(string[] args)
{
    var source = OptionValue(args, "--source");
    var outDir = OptionValue(args, "--out");
    if (source is null || outDir is null) return Fail("ir needs --source <dir> and --out <dir>.");

    var configPath = OptionValue(args, "--config") ?? FindEnginesJson();
    if (configPath is null || !File.Exists(configPath)) return Fail("engines.json not found. Pass --config <path>.");

    var load = EngineLoader.Load(configPath);
    foreach (var d in load.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Info))
        Console.Error.WriteLine($"[{d.Severity.ToString().ToUpperInvariant()}] {d.Message}");

    var engineId = OptionValue(args, "--engine");
    var engine = engineId is null ? load.Registry.Sources.FirstOrDefault() : load.Registry.FindSource(engineId);
    if (engine is null) return Fail(engineId is null ? "No source engine is loaded." : $"Source engine '{engineId}' is not loaded.");

    Migrator.Engine.Contracts.ProjectAnalysis analysis;
    try
    {
        analysis = engine.Analyzer.Analyze(source);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotImplementedException)
    {
        return Fail($"Cannot analyze '{source}': {ex.Message}");
    }
    Console.WriteLine($"{engine.Technology}: {analysis.Forms.Count} form(s), {analysis.UserControls.Count} user control(s); " +
                      $"startup: {analysis.StartupForm ?? "(not found)"}; framework: {analysis.FrameworkVersion ?? "(unknown)"}");

    var json = new JsonSerializerOptions { WriteIndented = true }; // PascalCase, like the existing IR samples
    var failed = 0;
    foreach (var result in SourceIrRunner.Run(engine, analysis))
    {
        if (result.Page is null)
        {
            failed++;
            Console.Error.WriteLine($"  FAILED {result.Unit.Name}: {result.Error}");
            continue;
        }
        var dir = Path.Combine(outDir, "ir", result.Unit.Name);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{result.Unit.Name}.complete.ir.json");
        File.WriteAllText(file, JsonSerializer.Serialize(result.Page, json));
        Console.WriteLine($"  {result.Unit.Kind,-11} {result.Unit.Name,-28} -> {file}");
    }
    return failed > 0 ? 2 : 0;
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
