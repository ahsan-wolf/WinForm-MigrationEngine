using Migrator.Engine.Contracts;
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
                                                  Extract IR only (no generation); writes the six IR files per unit under <out>/ir/<Form>/ plus <out>/ir/project.ir.json

              migrate --source <dir> --out <dir> [--target react-app] [--entry <form>] [--depth <n|all>] [--config <engines.json>]
                                                  ir step, then generate an app into <out>/<target> (default depth 1: entry form + forms it opens)
            """);
        return args.Length == 0 ? 1 : 0;
    }

    return args[0] switch
    {
        "engines" => ListEngines(args[1..]),
        "ir" => ExtractIr(args[1..]),
        "migrate" => Migrate(args[1..]),
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

    var results = SourceIrRunner.Run(engine, analysis);
    foreach (var failure in results.Where(r => r.Page is null))
        Console.Error.WriteLine($"  FAILED {failure.Unit.Name}: {failure.Error}");

    var summary = IrFileWriter.Write(outDir, engine, analysis, results);
    var ok = results.Count(r => r.Page is not null);
    Console.WriteLine($"  {ok} unit(s) extracted; {summary.Files.Count} IR file(s) written");
    Console.WriteLine($"  index: {summary.ProjectIrPath}");
    var failed = results.Count - ok;
    return failed > 0 ? 2 : 0;
}

static int Migrate(string[] args)
{
    var outDir = OptionValue(args, "--out");
    if (OptionValue(args, "--source") is null || outDir is null) return Fail("migrate needs --source <dir> and --out <dir>.");

    // Stage 1: IR (same as `ir`). Per-unit failures (exit 2) do not stop generation; real errors (exit 1) do.
    var irExit = ExtractIr(args);
    if (irExit == 1) return 1;

    var configPath = OptionValue(args, "--config") ?? FindEnginesJson();
    if (configPath is null || !File.Exists(configPath)) return Fail("engines.json not found. Pass --config <path>.");

    var targetId = OptionValue(args, "--target") ?? "react-app";
    var load = EngineLoader.Load(configPath);
    foreach (var d in load.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Info))
        Console.Error.WriteLine($"[{d.Severity.ToString().ToUpperInvariant()}] {d.Message}");
    if (load.Registry.FindTarget(targetId) is not IProjectTargetEngine target)
        return Fail($"Target engine '{targetId}' is not loaded or cannot generate a whole project. Check engines.json.");

    var depthText = OptionValue(args, "--depth");
    int? depth = depthText is null ? 1 : depthText.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : int.TryParse(depthText, out var d1) ? d1 : 1;

    GenerationResult result;
    try
    {
        result = target.GenerateProject(Path.Combine(outDir, "ir"), Path.Combine(outDir, targetId), new GenerationOptions { EntryForm = OptionValue(args, "--entry"), Depth = depth });
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException)
    {
        return Fail($"Generation failed: {ex.Message}");
    }

    Console.WriteLine($"{targetId}: generated {result.GeneratedUnits.Count} unit(s): {string.Join(", ", result.GeneratedUnits)}");
    Console.WriteLine($"  not generated: {result.SkippedUnits.Count}; {result.Files.Count} file(s) -> {result.OutputDirectory}");
    foreach (var group in result.Issues.GroupBy(i => i.Category).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {group.Key,-22} {group.Count()}");
    Console.WriteLine($"  report: {Path.Combine(result.OutputDirectory, "MIGRATION_REPORT.md")}");
    return result.Issues.Any(i => i.Category == "generation-failed") ? 2 : 0;
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
