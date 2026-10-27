using System.Text;
using System.Text.RegularExpressions;
using Migrator.Engine.Contracts;
using Migrator.Engine.Support;
using Migrator.Target.Api.Schema;
using Migrator.Target.Core.Generation;
using Migrator.Target.Core.Ir;
using Migrator.Target.Core.Translation;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.Api.Generation;

/// <summary>
/// IR folder -> a .NET 10 minimal API project (`<out>/dotnet-api`): the server-bound classes of the project's support code, ported (original logic, shims for the Windows
/// and configuration calls, provider aliases), one POST operation per public method, the runtime that carries object state and collects messages, and a report.
/// `dotnet build` is the gate: a member that does not compile is demoted to a `NotSupportedException` and listed.
/// </summary>
public sealed partial class ApiProjectGenerator
{
    private const int MaxRounds = 6;
    private readonly ApiTargetOptions _options;

    public ApiProjectGenerator(ApiTargetOptions options) => _options = options;

    public GenerationResult Run(string irFolder, string outputDirectory, GenerationOptions options)
    {
        var ir = IrFolder.Load(irFolder);
        var serverRules = ServerBoundRules.Load(_options.ServerBoundRulesPath);
        var operations = ServerOperations.Find(ir.Support, serverRules);
        if (operations.Count == 0)
            throw new InvalidOperationException("No server-bound classes found: nothing to put behind an API (a class needs at least two methods and half of them using data access, file or configuration code; see rules/server-bound.json).");

        var portRules = ApiPortRules.Load(_options.ApiPortRulesPath);
        var providerKey = options.DbProvider ?? portRules.Provider;
        if (!portRules.Providers.TryGetValue(providerKey, out var provider))
            throw new InvalidOperationException($"Unknown database provider '{providerKey}' (rules/api-port.json knows: {string.Join(", ", portRules.Providers.Keys)}).");

        var classes = Reachable(ir.Support!.Classes.Where(c => !c.External).ToList(), operations);
        var included = new HashSet<string>(classes.Select(c => c.Name), StringComparer.Ordinal);

        // The database structure behind the SQL, and the repository layer on top of it (sqlclient only: the repositories use Microsoft.Data.SqlClient directly).
        var schema = SqlSchemaExtractor.Extract(classes, SchemaInferRules.Load(_options.SchemaInferRulesPath));
        var withRepositories = schema.Tables.Count > 0 && provider.Package.Equals("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase);
        var data = withRepositories ? new DataLayer(RepositoryGenerator.Files(schema, RepositoryGenerator.DefaultDatabase, ConnectionKeys(classes)), SchemaWriters.Ddl(schema)) : null;
        var result = new GenerationResult { OutputDirectory = outputDirectory };
        var demoted = new HashSet<string>(StringComparer.Ordinal);
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var skippedOperations = new HashSet<string>(StringComparer.Ordinal);

        List<CompilerDiagnostic>? remaining = null;
        string gateNote;
        var rounds = 0;
        List<PortedFile> ported;
        List<(string Route, int Line)> endpointLines;
        while (true)
        {
            ported = classes.Select(c => ClassPorter.Port(c, portRules, provider, included, included, demoted, reasons)).ToList();
            var endpoints = WriteEndpoints(operations, skippedOperations, result);
            endpointLines = endpoints.Lines;
            WriteProject(outputDirectory, provider, portRules, ported, endpoints.Source, data, result);

            var diagnostics = CSharpGate.Build(outputDirectory, out var log);
            if (diagnostics is null)
            {
                gateNote = "not run: `dotnet build` could not produce a result (" + FirstLine(log) + ")";
                remaining = null;
                break;
            }
            remaining = diagnostics;
            if (diagnostics.Count == 0) { gateNote = rounds == 0 ? "clean on the first build" : $"clean after {rounds} regeneration round(s)"; break; }
            if (rounds >= MaxRounds) { gateNote = $"{diagnostics.Count} error(s) remain after {rounds} round(s)"; break; }

            var changed = false;
            foreach (var d in diagnostics)
            {
                if (d.File.EndsWith("Endpoints.cs", StringComparison.Ordinal))
                {
                    if (endpointLines.FirstOrDefault(e => e.Line == d.Line) is { Route: not null } hit && hit.Route.Length > 0 && skippedOperations.Add(hit.Route))
                    { reasons.Add($"{hit.Route}|{d.Code}: {d.Message}"); changed = true; }
                    continue;
                }
                var file = ported.FirstOrDefault(p => d.File.EndsWith(p.Path, StringComparison.Ordinal));
                var member = file?.Members.FirstOrDefault(m => m.StartLine <= d.Line && d.Line <= m.EndLine);
                if (member is null || !demoted.Add(member.Key)) continue;
                reasons.Add($"{member.Key}|{d.Code}: {Short(d.Message)}");
                changed = true;
            }
            if (!changed) { gateNote = $"{diagnostics.Count} error(s) outside ported members remain"; break; }
            rounds++;
        }

        var findings = Findings(classes, operations, serverRules, portRules, result);
        OutputFiles.Put(outputDirectory, "API_REPORT.md", Report(operations, classes, demoted, reasons, skippedOperations, findings, gateNote, remaining, providerKey, provider), result.Files);
        OutputFiles.Put(outputDirectory, "README.md", Readme(providerKey, withRepositories), result.Files);
        if (schema.Tables.Count > 0)
        {
            OutputFiles.Put(outputDirectory, "schema.sql", SchemaWriters.Ddl(schema), result.Files);
            OutputFiles.Put(outputDirectory, "SCHEMA_REPORT.md", SchemaWriters.Report(schema, withRepositories, withRepositories ? null : "The repository layer needs `--db-provider sqlclient`."), result.Files);
        }

        foreach (var c in operations) result.GeneratedUnits.Add(c.Name);
        var distinct = result.Files.Distinct(StringComparer.Ordinal).ToList();
        result.Files.Clear();
        result.Files.AddRange(distinct);
        result.Stats["api.classes"] = classes.Count;
        result.Stats["api.server-classes"] = operations.Count;
        result.Stats["api.operations"] = operations.Sum(c => c.Methods.Count(m => !m.HasByRefParameters)) - skippedOperations.Count;
        result.Stats["api.demoted-members"] = demoted.Count;
        result.Stats["api.schema-tables"] = schema.Tables.Count;
        result.Stats["api.schema-columns"] = schema.Tables.Sum(t => t.Columns.Count(c => !c.IsSurrogate));
        result.Stats["api.repositories"] = withRepositories ? schema.Tables.Count : 0;
        result.Stats["api.compile-errors-remaining"] = remaining?.Count ?? -1;
        result.Stats["api.sql-concatenation-findings"] = findings.Count(f => f.Category == "sql-concatenation");
        return result;
    }

    /// <summary>The server-bound classes and every project class they mention (types, bodies, base types), transitively: the rest of the support code (UI helpers, report wrappers) stays out of the API.</summary>
    private static List<SupportClassIr> Reachable(List<SupportClassIr> all, IReadOnlyList<ServerClass> operations)
    {
        var byName = all.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var reached = new HashSet<string>(operations.Select(o => o.Name), StringComparer.Ordinal);
        var queue = new Queue<SupportClassIr>(reached.Select(n => byName[n]));
        while (queue.Count > 0)
        {
            var cls = queue.Dequeue();
            var text = new StringBuilder(cls.BaseType ?? "");
            foreach (var m in cls.Members)
            {
                text.Append(' ').Append(m.Type).Append(' ').Append(m.Body);
                foreach (var p in m.Parameters) text.Append(' ').Append(p.Type);
            }
            foreach (Match w in Regex.Matches(text.ToString(), @"\b[A-Za-z_]\w*\b"))
                if (byName.TryGetValue(w.Value, out var next) && reached.Add(next.Name)) queue.Enqueue(next);
        }
        return all.Where(c => reached.Contains(c.Name)).ToList();
    }

    // ------------------------------------------------------------------------------------------------ files

    private sealed record DataLayer(List<(string Path, string Content)> Files, string Ddl);

    private static void WriteProject(string dir, ProviderRules provider, ApiPortRules rules, List<PortedFile> ported, string endpoints, DataLayer? data, GenerationResult result)
    {
        var extra = data is null ? "" : "<ItemGroup><None Include=\"Data/schema.sql\" CopyToOutputDirectory=\"PreserveNewest\" /></ItemGroup>";
        OutputFiles.Put(dir, "GeneratedApi.csproj", ApiTemplates.Project.Replace("__PROVIDER_PACKAGE__", provider.Package).Replace("__PROVIDER_VERSION__", provider.Version).Replace("__EXTRA_ITEMS__", extra), result.Files);
        OutputFiles.Put(dir, "appsettings.json", ApiTemplates.AppSettings, result.Files);
        var program = ApiTemplates.Program
            .Replace("__DATA_USINGS__", data is null ? "" : ApiTemplates.DataUsings)
            .Replace("__DATA_BUILDER__", data is null ? "" : ApiTemplates.DataBuilder)
            .Replace("__DATA_APP__", data is null ? "" : ApiTemplates.DataApp)
            .Replace("__DATA_MAP__", data is null ? "" : ApiTemplates.DataMap);
        OutputFiles.Put(dir, "Program.cs", program, result.Files);
        if (data is not null)
        {
            foreach (var (path, content) in data.Files) OutputFiles.Put(dir, path, content, result.Files);
            OutputFiles.Put(dir, "Data/schema.sql", data.Ddl, result.Files);
        }
        OutputFiles.Put(dir, "Api/Shims.cs", ApiTemplates.Shims, result.Files);
        var strip = "new string[] { " + string.Join(", ", provider.StripConnectionStringKeys.Select(k => "\"" + k + "\"")) + " }";
        OutputFiles.Put(dir, "Api/ApiRuntime.cs", ApiTemplates.Runtime.Replace("__STRIP_KEYS__", strip), result.Files);
        OutputFiles.Put(dir, "Endpoints.cs", endpoints, result.Files);
        OutputFiles.Put(dir, ".gitignore", "bin/\nobj/\nappsettings.*.json\n*.user\n", result.Files);
        foreach (var file in ported) OutputFiles.Put(dir, file.Path, file.Source, result.Files);
    }

    private static (string Source, List<(string Route, int Line)> Lines) WriteEndpoints(IReadOnlyList<ServerClass> operations, ISet<string> skipped, GenerationResult result)
    {
        var lines = new List<string>
        {
            "// Generated by Migrator.Target.Api: one POST operation per public method of a server-bound class. Body: { \"state\": {...}, \"args\": [...] }.",
            "using System.Text.Json;",
            "namespace Generated.Api;",
            "",
            "public static class Endpoints",
            "{",
            "    public static void Map(WebApplication app)",
            "    {",
        };
        var routeLines = new List<(string, int)>();
        foreach (var cls in operations)
        {
            lines.Add($"        // {cls.Name}");
            foreach (var m in cls.Methods.Where(m => !m.HasByRefParameters))
            {
                var route = $"/api/{cls.Name}/{m.Route}";
                if (skipped.Contains(route)) { lines.Add($"        // {route}: not exposed (see API_REPORT.md)"); continue; }
                var args = string.Join(", ", m.Parameters.Select((p, i) => $"ApiRuntime.Arg<{p.Type}>(a, {i})"));
                var call = m.IsStatic ? $"{cls.Name}.{m.Name}({args})" : $"o.{m.Name}({args})";
                var isTask = m.ReturnType.StartsWith("Task", StringComparison.Ordinal);
                var returnsValue = m.ReturnType.Trim() is not ("void" or "Task");
                var body = isTask
                    ? (returnsValue ? $"return (object)await {call};" : $"await {call}; return null;")
                    : (returnsValue ? $"return (object){call};" : $"{call}; return null;");
                var lambda = m.IsStatic ? $"ApiRuntime.RunStatic(call, async a => {{ {body} }})" : $"ApiRuntime.Run<{cls.Name}>(call, async (o, a) => {{ {body} }})";
                lines.Add($"        app.MapPost(\"{route}\", (CallRequest call) => {lambda});");
                routeLines.Add((route, lines.Count));
            }
        }
        lines.Add("    }");
        lines.Add("}");
        return (string.Join("\n", lines) + "\n", routeLines);
    }

    // ------------------------------------------------------------------------------------------------ findings and report

    private sealed record Finding(string Category, string Where, string Message);

    private static List<Finding> Findings(List<SupportClassIr> classes, IReadOnlyList<ServerClass> operations, ServerBoundRules serverRules, ApiPortRules rules, GenerationResult result)
    {
        var findings = new List<Finding>();
        var serverNames = operations.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        var secret = new Regex(rules.SecretSettingPattern, RegexOptions.IgnoreCase);
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var cls in classes)
        {
            foreach (var m in cls.Members.Where(m => m.Kind == MemberKind.Method && m.Body != null))
            {
                var where = $"{cls.Name}.{m.Name}";
                var body = m.Body!;
                // SQL assembled from text and values: an injection risk the port keeps (parameterization is a later step).
                if (Regex.IsMatch(body, @"(SELECT|INSERT|UPDATE|DELETE)\b[^;]*\bFROM|INSERT\s+INTO|UPDATE\s+\w+\s+SET|DELETE\s+FROM|\bselect\b.+\bfrom\b", RegexOptions.IgnoreCase) && Regex.IsMatch(body, @"string\.Concat|string\.Format|\+\s*[\w\.\(\[\]]+\s*\+|""\s*\+|\+\s*""|\$"""))
                    findings.Add(new Finding("sql-concatenation", where, "SQL text is built from strings and values (string.Concat / +): SQL-injection risk; parameterize before production use"));
                foreach (Match k in Regex.Matches(body, @"AppSettings\s*\[\s*""([^""]+)""\s*\]")) keys.Add(k.Groups[1].Value);
                if (Regex.IsMatch(body, @"\b(Application|Cursor|Form|Clipboard|Registry|Process)\.\w+|\bnew\s+(Form|Process)\b|Screen\.|SendKeys"))
                    findings.Add(new Finding("windows-only", where, "uses a Windows UI or OS API that has no server equivalent: the member is demoted if it does not compile, else review it"));
                if (Regex.IsMatch(body, @"#\s*\d|\bNow\(\)|\bIIf\(", RegexOptions.IgnoreCase) && Regex.IsMatch(body, @"OleDb|Select|Insert", RegexOptions.IgnoreCase))
                    findings.Add(new Finding("provider-sql", where, "SQL may use Access/Jet syntax (#date#, Now(), IIf): check it against the target database"));
            }
            if (cls.Unsupported.Count > 0) findings.Add(new Finding("not-extracted", cls.Name, $"{cls.Unsupported.Count} member(s) were not extracted by the source engine (events, indexers, operators): {string.Join("; ", cls.Unsupported.Take(3))}"));
        }
        foreach (var key in keys)
            findings.Add(new Finding(secret.IsMatch(key) ? "server-only-setting" : "setting", key, secret.IsMatch(key)
                ? "configuration key that looks like a secret: set it as AppSettings__" + key + " (environment) or user-secrets; never in a file"
                : "configuration key read by the ported code: set it as AppSettings__" + key));
        foreach (var f in findings.Where(f => f.Category is "sql-concatenation" or "windows-only" or "provider-sql").Take(200))
            result.Issues.Add(new GenerationIssue(f.Where, f.Category, f.Message));
        return findings;
    }

    private static string Report(IReadOnlyList<ServerClass> operations, List<SupportClassIr> classes, ISet<string> demoted, ISet<string> reasons, ISet<string> skipped,
        List<Finding> findings, string gateNote, List<CompilerDiagnostic>? remaining, string providerKey, ProviderRules provider)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# API report — dotnet-api");
        sb.AppendLine();
        sb.AppendLine($"- Provider: **{providerKey}** (`{provider.Package}` {provider.Version}); `OleDb*` types are aliased to it where the provider differs.");
        sb.AppendLine($"- Ported: **{classes.Count}** class(es) of the project's support code; **{operations.Count}** server-bound class(es) expose **{operations.Sum(c => c.Methods.Count(m => !m.HasByRefParameters)) - skipped.Count}** operation(s).");
        sb.AppendLine($"- Compile gate (`dotnet build`): {gateNote}.");
        sb.AppendLine();
        sb.AppendLine("This is the original data-access logic ported mechanically: **not reviewed, not parameterized, not secured.** Run it only behind authentication, with a database account of least privilege.");
        sb.AppendLine();
        sb.AppendLine("## Run");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("set AppSettings__<key>=<value>        # the keys listed below (connection string and the like); never commit them");
        sb.AppendLine("dotnet run                            # http://localhost:5000  (see launch settings), GET /api/health");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Operations");
        sb.AppendLine();
        sb.AppendLine("`POST /api/<Class>/<Method>` with `{ \"state\": { ...public data of the object... }, \"args\": [ ... ] }` -> `{ result, state, messages }`; errors -> `{ error, messages }` (500).");
        sb.AppendLine();
        sb.AppendLine("| Class | Operations | State members |");
        sb.AppendLine("|---|---|---|");
        foreach (var c in operations)
            sb.AppendLine($"| {c.Name}{(c.IsStatic ? " (static)" : "")} | {string.Join(", ", c.Methods.Where(m => !m.HasByRefParameters).Select(m => $"`{m.Route}`"))} | {c.State.Count} |");
        var byRef = operations.SelectMany(c => c.Methods.Where(m => m.HasByRefParameters).Select(m => $"{c.Name}.{m.Name}")).ToList();
        if (byRef.Count > 0) sb.AppendLine().AppendLine($"Not exposed (ref / out parameters): {string.Join(", ", byRef)}.");

        if (demoted.Count > 0 || skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Not ported (demoted by the compile gate)");
            sb.AppendLine();
            sb.AppendLine("These members did not compile on .NET 10; they throw `NotSupportedException` and keep the original as a comment.");
            sb.AppendLine();
            foreach (var r in reasons.OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine($"- `{r.Split('|')[0]}`: {r[(r.IndexOf('|') + 1)..]}");
        }
        if (remaining is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Compile errors remaining");
            sb.AppendLine();
            foreach (var d in remaining.Take(40)) sb.AppendLine($"- `{d.File}({d.Line})` {d.Code}: {d.Message}");
        }

        foreach (var (title, category) in new[] { ("SQL built from strings (injection risk)", "sql-concatenation"), ("Windows-only or UI calls", "windows-only"), ("Provider-specific SQL", "provider-sql"), ("Not extracted by the source engine", "not-extracted") })
        {
            var list = findings.Where(f => f.Category == category).ToList();
            if (list.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"## {title} ({list.Count})");
            sb.AppendLine();
            foreach (var f in list.Take(120)) sb.AppendLine($"- `{f.Where}`: {f.Message}");
            if (list.Count > 120) sb.AppendLine($"- ... and {list.Count - 120} more");
        }

        var settings = findings.Where(f => f.Category is "setting" or "server-only-setting").ToList();
        if (settings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Configuration the ported code reads");
            sb.AppendLine();
            sb.AppendLine("| Key | Environment variable | Note |");
            sb.AppendLine("|---|---|---|");
            foreach (var f in settings) sb.AppendLine($"| `{f.Where}` | `AppSettings__{f.Where}` | {(f.Category == "server-only-setting" ? "**SERVER-ONLY**: looks like a secret" : "plain setting")} |");
        }
        return sb.ToString();
    }

    /// <summary>The `AppSettings` keys whose value the code uses as a connection string (read in a method that also sets or opens a connection).</summary>
    private static List<string> ConnectionKeys(List<SupportClassIr> classes)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var m in classes.SelectMany(c => c.Members).Where(m => m.Kind == MemberKind.Method && m.Body != null && m.Body.Contains("Connection", StringComparison.Ordinal)))
            foreach (Match k in Regex.Matches(m.Body!, @"AppSettings\s*\[\s*""([^""]+)""\s*\]"))
                keys.Add(k.Groups[1].Value);
        return keys.ToList();
    }

    private static string Readme(string providerKey, bool withRepositories) => $"""
        # Generated API

        A .NET 10 minimal API generated by `migrator migrate --target dotnet-api`. It hosts the data-access classes of the migrated application and exposes their public methods as `POST /api/<Class>/<Method>` operations (see `API_REPORT.md`).

        ```bash
        # configuration: environment variables or user-secrets, never files
        set AppSettings__path=<connection string>      # example key; the keys are listed in API_REPORT.md
        dotnet run
        ```

        Provider: `{providerKey}`. Allowed browser origins: `Cors:Origins` in `appsettings.json`.
        {(withRepositories ? RepositoryReadme : "")}
        """;

    private const string RepositoryReadme = """

        ## Database and repositories
        `schema.sql` is the database structure **inferred** from the SQL of the original code (`SCHEMA_REPORT.md`). Each table has an entity, a repository (`Data/Repositories`) and REST endpoints `GET|POST /api/data/<table>`, `GET|PUT|DELETE /api/data/<table>/<key>`, `GET /api/data/<table>/next-id`; the repositories use parameterized SQL only.

        ```bash
        # SQL Server LocalDB (installed with Visual Studio / SQL Server Express): nothing to configure, the database is created on first start
        dotnet run
        # another server: set the connection string in the environment (never in a file); the database is created only if Database__AutoCreate=true
        set ConnectionStrings__Default=Server=...;Database=...;...
        ```
        The ported data classes are pointed at the same database through their old `AppSettings` connection key.
        """;

    private static string Short(string message) => message.Length > 160 ? message[..160] + "..." : message;

    private static string FirstLine(string log) => log.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "no output";
}
