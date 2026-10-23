using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using MigratorUi.Host;

// Local host of the Migrator UI: serves the React UI (built with @wfm/ui-react) and runs the migrator CLI and the generated apps as child processes.
// Bound to loopback only; every POST must carry the X-Migrator-UI header (no CORS is enabled, so a web page on another origin cannot send it).

var builder = WebApplication.CreateBuilder(args);
var url = args.SkipWhile(a => a != "--urls").Skip(1).FirstOrDefault() ?? "http://127.0.0.1:5280";
builder.WebHost.UseUrls(url);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

var repoRoot = FindRepoRoot();
var enginesJson = repoRoot is null ? null : Path.Combine(repoRoot, "engines.json");
var cliDll = Path.Combine(AppContext.BaseDirectory, "migrator.dll");
var runs = new ConcurrentDictionary<string, Run>();
var outputRoots = new ConcurrentBag<string>();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// The UI build: tools/migrator-ui/web/dist next to this project (npm run build in web/), or a wwwroot beside the executable.
var webDist = repoRoot is null ? null : Path.Combine(repoRoot, "tools", "migrator-ui", "web", "dist");
var staticRoot = webDist is not null && Directory.Exists(webDist) ? webDist : Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(staticRoot))
{
    var files = new PhysicalFileProvider(staticRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
}

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Method == "POST" && !ctx.Request.Headers.ContainsKey("X-Migrator-UI"))
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsync("Missing X-Migrator-UI header.");
        return;
    }
    await next();
});

app.MapGet("/api/info", () => new
{
    repoRoot,
    enginesJson,
    cliAvailable = File.Exists(cliDll),
    uiBuilt = Directory.Exists(staticRoot),
    platform = OperatingSystem.IsWindows() ? "windows" : "other",
    targets = new[]
    {
        new { id = "react-app", modes = new[] { "ir", "app", "appshell", "viewer" } },
        new { id = "angular-app", modes = new[] { "ir", "app" } },
    },
});

app.MapGet("/api/browse", (string? path) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        var roots = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
        return Results.Json(new { path = "", parent = (string?)null, dirs = roots, files = Array.Empty<string>() }, json);
    }
    try
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) return Results.NotFound(new { error = "Folder not found." });
        var dirs = Directory.EnumerateDirectories(full).Select(Path.GetFileName).Where(n => n is not null && !n.StartsWith('.') && n != "node_modules").OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var markers = Directory.EnumerateFiles(full, "*.csproj").Concat(Directory.EnumerateFiles(full, "*.sln")).Select(Path.GetFileName).ToList();
        return Results.Json(new { path = full, parent = Directory.GetParent(full)?.FullName ?? "", dirs, files = markers }, json);
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/migrate", (MigrateRequest r) =>
{
    if (!File.Exists(cliDll)) return Results.BadRequest(new { error = "migrator.dll is not next to the host. Build the host project." });
    if (enginesJson is null || !File.Exists(enginesJson)) return Results.BadRequest(new { error = "engines.json not found above the host." });
    if (string.IsNullOrWhiteSpace(r.Source) || !Directory.Exists(r.Source)) return Results.BadRequest(new { error = "The source folder does not exist." });
    if (string.IsNullOrWhiteSpace(r.Out)) return Results.BadRequest(new { error = "Choose an output folder." });
    var outFull = Path.GetFullPath(r.Out);
    if (repoRoot is not null && outFull.StartsWith(repoRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "The output folder must be outside this repository." });

    var cli = BuildArgs(r, outFull, enginesJson, out var appDir);
    outputRoots.Add(outFull);
    var run = new Run
    {
        Kind = "migrate",
        Title = $"{Mode(r.Mode)} — {Path.GetFileName(r.Source.TrimEnd('\\', '/'))}",
        CommandLine = "migrator " + string.Join(' ', cli.Select(Quote)),
        OutputDirectory = outFull,
        AppDirectory = appDir,
        WorkingDirectory = repoRoot,
    };
    runs[run.Id] = run;
    run.Add("> " + run.CommandLine);
    run.Start("dotnet", new[] { cliDll }.Concat(cli), repoRoot);
    return Results.Json(Describe(run), json);
});

app.MapPost("/api/app/install", (AppRequest r) => StartApp(r, "install"));
app.MapPost("/api/app/start", (AppRequest r) => StartApp(r, "start"));

IResult StartApp(AppRequest r, string action)
{
    var dir = r.Dir is null ? null : Path.GetFullPath(r.Dir);
    if (dir is null || !IsGeneratedApp(dir)) return Results.BadRequest(new { error = "That folder is not a generated app (no generated package.json)." });

    if (action == "start")
    {
        var existing = runs.Values.FirstOrDefault(x => x.Kind == "dev" && !x.Done && string.Equals(x.AppDirectory, dir, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return Results.Json(Describe(existing), json);
        if (!Directory.Exists(Path.Combine(dir, "node_modules"))) return Results.BadRequest(new { error = "Dependencies are not installed. Run Install first." });
    }

    var run = new Run { Kind = action == "install" ? "install" : "dev", Title = (action == "install" ? "npm install — " : "dev server — ") + Path.GetFileName(dir), AppDirectory = dir, WorkingDirectory = dir };
    if (action == "install")
    {
        var (file, a) = Run.Shell("npm", "install");
        runs[run.Id] = run;
        run.Add($"> npm install   (in {dir})");
        run.Start(file, a, dir);
        return Results.Json(Describe(run), json);
    }

    var port = r.Port ?? Enumerable.Range(5400, 100).First(Run.PortFree);
    var angular = File.Exists(Path.Combine(dir, "angular.json"));
    var (f, args2) = angular
        ? Run.Shell("npx", "ng", "serve", "--port", port.ToString())
        : Run.Shell("npm", "run", "dev", "--", "--port", port.ToString(), "--strictPort");
    run = new Run { Kind = "dev", Title = run.Title, AppDirectory = dir, WorkingDirectory = dir, CommandLine = angular ? $"npx ng serve --port {port}" : $"npm run dev -- --port {port} --strictPort", Port = port };
    runs[run.Id] = run;
    run.Add($"> {run.CommandLine}   (in {dir})");
    run.Start(f, args2, dir);
    var started = run;
    _ = Task.Run(async () => { started.Ready = await Run.WaitForPort(port, TimeSpan.FromSeconds(180), () => started.Done); });
    return Results.Json(Describe(run), json);
}

app.MapGet("/api/runs", () => runs.Values.OrderByDescending(x => x.Started).Select(Describe));
app.MapGet("/api/runs/{id}", (string id) => runs.TryGetValue(id, out var r) ? Results.Json(Describe(r), json) : Results.NotFound());
app.MapPost("/api/runs/{id}/stop", (string id) =>
{
    if (!runs.TryGetValue(id, out var r)) return Results.NotFound();
    r.Stop();
    return Results.Json(Describe(r), json);
});

// Server-sent events: every output line as a JSON string; `event: done` when the process has ended.
app.MapGet("/api/runs/{id}/log", async (string id, HttpContext ctx) =>
{
    if (!runs.TryGetValue(id, out var run)) { ctx.Response.StatusCode = 404; return; }
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    var index = 0;
    while (!ctx.RequestAborted.IsCancellationRequested)
    {
        var lines = run.LinesFrom(index);
        foreach (var line in lines) await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(line)}\n\n");
        index += lines.Count;
        if (run.Done && run.LineCount == index)
        {
            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
            await ctx.Response.Body.FlushAsync();
            return;
        }
        await ctx.Response.Body.FlushAsync();
        try { await Task.Delay(250, ctx.RequestAborted); } catch (TaskCanceledException) { return; }
    }
});

// MIGRATION_REPORT.md of an output folder (the app folder itself, or one level up).
app.MapGet("/api/report", (string dir) =>
{
    var full = Path.GetFullPath(dir);
    foreach (var candidate in new[] { Path.Combine(full, "MIGRATION_REPORT.md"), Path.Combine(full, "react-app", "MIGRATION_REPORT.md"), Path.Combine(full, "angular-app", "MIGRATION_REPORT.md") })
        if (File.Exists(candidate) && (IsUnderOutput(candidate) || IsGeneratedApp(Path.GetDirectoryName(candidate)!)))
            return Results.Text(File.ReadAllText(candidate), "text/markdown; charset=utf-8");
    return Results.NotFound(new { error = "No MIGRATION_REPORT.md there (the IR-only mode writes none)." });
});

app.Lifetime.ApplicationStopping.Register(() => { foreach (var r in runs.Values) r.Stop(); });
Console.WriteLine($"Migrator UI: {url}   (repo: {repoRoot ?? "not found"}; UI: {(Directory.Exists(staticRoot) ? staticRoot : "not built — run npm run build in tools/migrator-ui/web")})");
app.Run();

// ---------------------------------------------------------------------------------------------------------

bool IsUnderOutput(string path) => outputRoots.Any(o => path.StartsWith(o, StringComparison.OrdinalIgnoreCase));

bool IsGeneratedApp(string dir)
{
    var package = Path.Combine(dir, "package.json");
    if (!File.Exists(package)) return false;
    var text = File.ReadAllText(package);
    // React apps depend on @wfm/ui-react; Angular apps get the library through tsconfig paths, so they are recognised by the generated app name.
    return text.Contains("@wfm/ui-", StringComparison.Ordinal) || text.Contains("\"name\": \"migrated-", StringComparison.Ordinal);
}

static object Describe(Run r) => new
{
    r.Id, r.Kind, r.Title, r.CommandLine, r.OutputDirectory, r.AppDirectory, r.Done, r.ExitCode, r.Cancelled, r.Port, r.Ready, url = r.Url, lines = r.LineCount,
    started = r.Started,
};

static string Mode(string? mode) => mode switch { "ir" => "IR only", "appshell" => "App shell", "viewer" => "Component viewer", _ => "Working app" };

static string Quote(string s) => s.Contains(' ') || s.Contains('"') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;

static List<string> BuildArgs(MigrateRequest r, string outFull, string enginesJson, out string appDir)
{
    var target = string.IsNullOrWhiteSpace(r.Target) ? "react-app" : r.Target;
    var mode = r.Mode ?? "app";
    var a = new List<string>();
    if (mode == "ir")
    {
        a.AddRange(new[] { "ir", "--source", r.Source!, "--out", outFull });
        appDir = Path.Combine(outFull, "ir");
        a.AddRange(new[] { "--config", enginesJson });
        return a;
    }
    a.AddRange(new[] { "migrate", "--source", r.Source!, "--out", outFull, "--target", target });
    var name = mode switch { "appshell" => target.Replace("-app", "") + "-appshell", "viewer" => target.Replace("-app", "") + "-viewer", _ => target };
    appDir = Path.Combine(outFull, name);
    if (mode == "appshell") a.Add("--appshell");
    if (mode == "viewer") a.Add("--preview");
    if (!string.IsNullOrWhiteSpace(r.Entry)) a.AddRange(new[] { "--entry", r.Entry });
    if (!string.IsNullOrWhiteSpace(r.Depth)) a.AddRange(new[] { "--depth", r.Depth });
    if (r.AllUnits) a.AddRange(new[] { "--units", "all" });
    if (r.Catalog) a.Add("--catalog");
    if (r.NoExternalProjects) a.Add("--no-external-projects");
    if (mode == "app")
    {
        if (!string.IsNullOrWhiteSpace(r.Translate)) a.AddRange(new[] { "--translate", r.Translate });
        if (r.CompileGate) a.Add("--compile-gate");
        foreach (var c in r.Contracts ?? new()) if (!string.IsNullOrWhiteSpace(c)) a.AddRange(new[] { "--contract", c.Trim() });
        if (!string.IsNullOrWhiteSpace(r.ContractScope)) a.AddRange(new[] { "--contract-scope", r.ContractScope });
    }
    if (!string.IsNullOrWhiteSpace(r.Images)) a.AddRange(new[] { "--images", r.Images });
    if (!string.IsNullOrWhiteSpace(r.Archetypes)) a.AddRange(new[] { "--archetypes", r.Archetypes });
    a.AddRange(new[] { "--config", enginesJson });
    return a;
}

static string? FindRepoRoot()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "engines.json")) && File.Exists(Path.Combine(dir.FullName, "WinFormsMigrator.sln"))) return dir.FullName;
    return null;
}

public sealed record MigrateRequest(
    string? Source, string? Out, string? Mode, string? Target, string? Entry, string? Depth, bool AllUnits, bool Catalog, bool NoExternalProjects,
    string? Translate, bool CompileGate, List<string>? Contracts, string? ContractScope, string? Images, string? Archetypes);

public sealed record AppRequest(string? Dir, int? Port);
