using System.Text;
using System.Text.Json;

namespace Migrator.Source.Backend;

/// <summary>Writes the analysis as JSON (machine-readable) and Markdown (human summary). Output belongs in a git-ignored folder.</summary>
public static class BackendReport
{
    public static void Write(BackendAnalysis a, string outDir)
    {
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "backend-analysis.json"), JsonSerializer.Serialize(a, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(outDir, "BACKEND_ANALYSIS.md"), Markdown(a));
    }

    public static string Markdown(BackendAnalysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Backend analysis").AppendLine().AppendLine("Read-only, syntax-only survey. Source text, connection strings and secrets are never copied here; only names, counts and locations.").AppendLine();

        sb.AppendLine("## Sources").AppendLine().AppendLine("| Folder | .cs files | Classes | .asmx | config files |").AppendLine("|---|---|---|---|---|");
        foreach (var s in a.Sources) sb.AppendLine($"| {s.Root} | {s.CsFiles} | {s.Classes} | {s.Asmx} | {s.ConfigFiles} |");

        sb.AppendLine().AppendLine($"## Services ({a.Services.Count}) — {a.Services.Sum(s => s.Operations.Count)} operations");
        foreach (var s in a.Services)
        {
            sb.AppendLine().AppendLine($"### {s.Name} ({s.Operations.Count} operations)").AppendLine()
              .AppendLine("| Operation | Returns | Statements | Calls | Data | Windows | Host | Config |").AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var o in s.Operations)
                sb.AppendLine($"| {o.Name} | {o.ReturnType} | {o.Statements} | {string.Join(", ", o.CallsClasses)} | {Flag(o.DataAccess)} | {Flag(o.Windows)} | {Flag(o.HostContext)} | {Flag(o.Config)} |");
        }

        sb.AppendLine().AppendLine($"## SOAP proxies ({a.Proxies.Count})").AppendLine().AppendLine("| Proxy | Base | Operations | Url assignments |").AppendLine("|---|---|---|---|");
        foreach (var p in a.Proxies) sb.AppendLine($"| {p.Name} | {p.BaseType} | {p.Operations.Count} ({string.Join(", ", p.Operations.Take(6))}{(p.Operations.Count > 6 ? ", …" : "")}) | {p.UrlAssignments} |");

        sb.AppendLine().AppendLine($"## Facades / helper classes ({a.Facades.Count})").AppendLine()
          .AppendLine("`thin` = public method of 8 or fewer statements that calls a SOAP proxy; `logic` = everything else. Calls to proxies are listed.").AppendLine()
          .AppendLine("| Class | Static | Methods | Thin | Logic | Data access | Windows | Proxies used |").AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var f in a.Facades.OrderByDescending(f => f.Methods)) sb.AppendLine($"| {f.Name} | {Flag(f.IsStatic)} | {f.Methods} | {f.Thin} | {f.Logic} | {f.DataAccess} | {f.Windows} | {string.Join(", ", f.Proxies)} |");

        sb.AppendLine().AppendLine("## Findings by category").AppendLine();
        foreach (var g in a.Findings.GroupBy(f => f.Category).OrderBy(g => g.Key))
        {
            sb.AppendLine($"### {g.Key} ({g.Count()})").AppendLine().AppendLine("| Pattern | Count | Classes |").AppendLine("|---|---|---|");
            foreach (var p in g.GroupBy(f => f.Pattern).OrderByDescending(p => p.Count()).Take(40))
                sb.AppendLine($"| {p.Key} | {p.Count()} | {string.Join(", ", p.Select(f => f.Class).Distinct().Take(8))} |");
            sb.AppendLine();
        }

        sb.AppendLine($"## Static mutable state ({a.StaticState.Count})").AppendLine().AppendLine("Becomes per-user session state (or a scoped service) in the REST API. Never a process-wide static.").AppendLine()
          .AppendLine("| Class | Member | Type | Kind |").AppendLine("|---|---|---|---|");
        foreach (var s in a.StaticState.Take(150)) sb.AppendLine($"| {s.Class} | {s.Name} | {s.Type} | {s.Kind} |");
        if (a.StaticState.Count > 150) sb.AppendLine($"| … {a.StaticState.Count - 150} more (see JSON) | | | |");

        sb.AppendLine().AppendLine("## Configuration keys (names only)").AppendLine();
        foreach (var c in a.Config) sb.AppendLine($"- {c.File}: appSettings {c.AppSettings.Count} ({string.Join(", ", c.AppSettings.Take(30))}); connection strings {c.ConnectionStrings.Count} ({string.Join(", ", c.ConnectionStrings)})");

        sb.AppendLine().AppendLine($"## WSDL ({a.Wsdls.Count})").AppendLine().AppendLine("| File | Services | Operations | Endpoint host(s) |").AppendLine("|---|---|---|---|");
        foreach (var w in a.Wsdls) sb.AppendLine($"| {Path.GetFileName(w.File)} | {string.Join(", ", w.Services)} | {w.Operations.Count} ({string.Join(", ", w.Operations.Take(6))}) | {string.Join(", ", w.Endpoints.Select(Host).Distinct())} |");

        if (a.Warnings.Count > 0) { sb.AppendLine().AppendLine("## Warnings").AppendLine(); foreach (var w in a.Warnings) sb.AppendLine("- " + w); }
        return sb.ToString();
    }

    private static string Flag(bool b) => b ? "yes" : "";
    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
}
