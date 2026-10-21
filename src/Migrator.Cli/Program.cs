using System.Text.Json;
using Migrator.Engine.Assemblies;
using Migrator.Engine.Contracts;
using Migrator.Engine.Hosting;
using Migrator.Engine.Pipeline;
using Migrator.Source.Backend;

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

              contract --assembly <dll> --out <dir>
                                                  Read a managed assembly's public API from metadata only (never loaded or run) -> <out>/<name>/contract.json
                                                  and data-access.json (SQL table/procedure names; connection-string-like literals are only counted)
              backend --source <dir>... [--wsdl <dir>...] --out <dir> [--rules <backend-patterns.json>]
                                                  Read-only survey of .NET Framework class libraries / ASMX services and WSDLs -> BACKEND_ANALYSIS.md + backend-analysis.json (use a git-ignored --out)
              migrate --source <dir> --out <dir> [--target react-app] [--entry <form>] [--depth <n|all>] [--units all] [--preview|--appshell] [--contract <contract.json>]... [--contract-scope used|all] [--translate none|rules|llm] [--compile-gate] [--config <engines.json>]
                                                  ir step, then generate an app into <out>/<target> (default depth 1: entry form + forms it opens)
            """);
        return args.Length == 0 ? 1 : 0;
    }

    return args[0] switch
    {
        "engines" => ListEngines(args[1..]),
        "ir" => ExtractIr(args[1..]),
        "migrate" => Migrate(args[1..]),
        "contract" => Contract(args[1..]),
        "backend" => Backend(args[1..]),
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
        analysis = engine.Analyzer.Analyze(source, new Migrator.Engine.Contracts.AnalysisOptions { IncludeReferencedProjects = !args.Contains("--no-external-projects") });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotImplementedException)
    {
        return Fail($"Cannot analyze '{source}': {ex.Message}");
    }
    Console.WriteLine($"{engine.Technology}: {analysis.Forms.Count} form(s), {analysis.UserControls.Count} user control(s); " +
                      $"startup: {analysis.StartupForm ?? "(not found)"}; framework: {analysis.FrameworkVersion ?? "(unknown)"}");

    if (analysis.AdditionalRoots is { Count: > 0 })
        Console.WriteLine($"  included {analysis.AdditionalRoots.Count} referenced project folder(s) outside the source folder (classes only; --no-external-projects to skip): {string.Join(", ", analysis.AdditionalRoots.Take(6).Select(r => Path.GetFileName(r.TrimEnd('/', '\\'))))}{(analysis.AdditionalRoots.Count > 6 ? ", ..." : "")}");

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

static int Contract(string[] args)
{
    var assembly = OptionValue(args, "--assembly");
    var outDir = OptionValue(args, "--out");
    if (assembly is null || outDir is null) return Fail("contract needs --assembly <dll> and --out <dir> (use a git-ignored folder such as local-outputs).");
    if (!File.Exists(assembly)) return Fail($"Assembly not found: {assembly}");

    AssemblyContract contract;
    DataAccessHints hints;
    try
    {
        contract = MetadataContractReader.Read(assembly);
        hints = MetadataContractReader.ReadDataAccessHints(assembly);
    }
    catch (Exception ex) when (ex is InvalidDataException or BadImageFormatException or IOException)
    {
        return Fail($"Cannot read '{assembly}': {ex.Message}");
    }

    var dir = Path.Combine(outDir, contract.Name);
    Directory.CreateDirectory(dir);
    var json = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(Path.Combine(dir, "contract.json"), JsonSerializer.Serialize(contract, json));
    File.WriteAllText(Path.Combine(dir, "data-access.json"), JsonSerializer.Serialize(hints, json));

    Console.WriteLine($"{contract.Name} {contract.Version}: {contract.Types.Count} public type(s) in {contract.Types.Select(t => t.Namespace).Distinct().Count()} namespace(s)");
    Console.WriteLine("  " + string.Join(", ", contract.Types.GroupBy(t => t.Kind).Select(g => $"{g.Key} {g.Count()}")));
    Console.WriteLine($"  members: {contract.Types.Sum(t => t.Properties.Count)} properties, {contract.Types.Sum(t => t.Methods.Count)} methods, {contract.Types.Sum(t => t.EnumMembers.Count)} enum values");
    Console.WriteLine($"  data access: {hints.SqlLiteralCount} SQL literal(s), {hints.Tables.Count} table name(s), {hints.StoredProcedures.Count} procedure(s); {hints.ConnectionStringLikeCount} connection-string-like literal(s) discarded");
    Console.WriteLine($"  written: {dir}");
    return 0;
}

static int Backend(string[] args)
{
    var outDir = OptionValue(args, "--out");
    var sources = OptionValues(args, "--source");
    if (sources.Count == 0 || outDir is null) return Fail("backend needs at least one --source <dir> and --out <dir> (use a git-ignored folder such as local-outputs/).");

    var rulesPath = OptionValue(args, "--rules") ?? FindUpwards(Path.Combine("rules", "backend-patterns.json"));
    var analysis = new BackendAnalyzer(BackendAnalyzer.LoadRules(rulesPath)).Analyze(sources, OptionValues(args, "--wsdl"));
    BackendReport.Write(analysis, outDir);

    Console.WriteLine($"Sources: {analysis.Sources.Count}, classes {analysis.Sources.Sum(s => s.Classes)}; services {analysis.Services.Count} ({analysis.Services.Sum(s => s.Operations.Count)} operations); SOAP proxies {analysis.Proxies.Count}; WSDL {analysis.Wsdls.Count}");
    Console.WriteLine($"Facades {analysis.Facades.Count}; static mutable state {analysis.StaticState.Count}; findings: " + string.Join(", ", analysis.Findings.GroupBy(f => f.Category).Select(g => $"{g.Key} {g.Count()}")));
    foreach (var w in analysis.Warnings) Console.WriteLine("  warning: " + w);
    Console.WriteLine($"  written: {Path.GetFullPath(outDir)}");
    return 0;
}

static string? FindUpwards(string relative)
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
    return null;
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

    var appShell = args.Contains("--appshell");
    var preview = appShell || args.Contains("--preview");
    var outputName = appShell ? targetId.Replace("-app", "") + "-appshell" : preview ? targetId.Replace("-app", "") + "-viewer" : targetId;

    GenerationResult result;
    try
    {
        result = target.GenerateProject(Path.Combine(outDir, "ir"), Path.Combine(outDir, outputName), new GenerationOptions
        {
            EntryForm = OptionValue(args, "--entry"),
            Depth = depth,
            AllUnits = OptionValue(args, "--units")?.Equals("all", StringComparison.OrdinalIgnoreCase) == true,
            Preview = preview,
            AppShell = appShell,
            ContractFiles = OptionValues(args, "--contract"),
            ContractScope = OptionValue(args, "--contract-scope") ?? "used",
            ImagesFolder = OptionValue(args, "--images"),
            Translate = OptionValue(args, "--translate") ?? "none",
            CompileGate = args.Contains("--compile-gate"),
            NoCompileGate = args.Contains("--no-compile-gate")
        });
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException)
    {
        return Fail($"Generation failed: {ex.Message}");
    }

    Console.WriteLine(preview
        ? $"{outputName}: {result.GeneratedUnits.Count} visual-only page(s) for the {(appShell ? "app shell" : "component viewer")} ({result.SkippedUnits.Count} unit(s) could not be generated)"
        : $"{targetId}: generated {result.GeneratedUnits.Count} unit(s): {string.Join(", ", result.GeneratedUnits)}");
    Console.WriteLine($"  not generated: {result.SkippedUnits.Count}; {result.Files.Count} file(s) -> {result.OutputDirectory}");
    if (result.Stats.TryGetValue("methods", out var methods))
        Console.WriteLine($"  translation: {methods} method(s): {result.Stats["methods.complete"]} complete, {result.Stats["methods.partial"]} partial, " +
                          $"{result.Stats["methods.not-translated"]} not translated ({result.Stats["methods.demoted-by-gate"]} demoted by the compile gate); {result.Stats["gate.remaining-errors"]} TS error(s) remain");
    foreach (var group in result.Issues.GroupBy(i => i.Category).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {group.Key,-22} {group.Count()}");
    Console.WriteLine($"  report: {Path.Combine(result.OutputDirectory, preview ? "VIEWER_REPORT.md" : "MIGRATION_REPORT.md")}");
    return result.Issues.Any(i => i.Category == "generation-failed") ? 2 : 0;
}

static List<string> OptionValues(string[] args, string name)
{
    var values = new List<string>();
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name) values.Add(args[i + 1]);
    return values;
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
