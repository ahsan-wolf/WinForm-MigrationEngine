using System.Text.Json;

namespace Migrator.Target.Api;

/// <summary>One ADO.NET provider the ported code can run against (`rules/api-port.json`).</summary>
public sealed record ProviderRules(string Package, string Version, string Namespace, IReadOnlyDictionary<string, string> Aliases, IReadOnlyList<string> StripConnectionStringKeys);

/// <summary>`rules/api-port.json` read as data: usings, providers (with the type aliases that let `OleDb*` code compile against another provider) and the secret-looking setting names.</summary>
public sealed class ApiPortRules
{
    public string Provider { get; private set; } = "sqlclient";
    public IReadOnlyList<string> Usings { get; private set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, ProviderRules> Providers { get; private set; } = new Dictionary<string, ProviderRules>();
    public string SecretSettingPattern { get; private set; } = "(pwd|pass(word)?|secret|token|key|user|uid|license|credential|connection)";

    public static ApiPortRules Load(string? path)
    {
        var rules = new ApiPortRules
        {
            Usings = new[] { "System", "System.Collections", "System.Collections.Generic", "System.Data", "System.Data.Common", "System.Linq", "System.Text" },
            Providers = new Dictionary<string, ProviderRules>
            {
                ["sqlclient"] = new("Microsoft.Data.SqlClient", "6.*", "Microsoft.Data.SqlClient", new Dictionary<string, string>
                {
                    ["OleDbConnection"] = "SqlConnection", ["OleDbCommand"] = "SqlCommand", ["OleDbDataAdapter"] = "SqlDataAdapter", ["OleDbDataReader"] = "SqlDataReader",
                    ["OleDbParameter"] = "SqlParameter", ["OleDbTransaction"] = "SqlTransaction", ["OleDbException"] = "SqlException", ["OleDbType"] = "System.Data.SqlDbType"
                }, new[] { "Provider" }),
            }
        };
        if (path is null || !File.Exists(path)) return rules;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.TryGetProperty("provider", out var provider)) rules.Provider = provider.GetString() ?? rules.Provider;
        if (root.TryGetProperty("usings", out var usings)) rules.Usings = usings.EnumerateArray().Select(u => u.GetString()!).ToList();
        if (root.TryGetProperty("secretSettingPattern", out var secret)) rules.SecretSettingPattern = secret.GetString() ?? rules.SecretSettingPattern;
        if (root.TryGetProperty("providers", out var providers))
        {
            var map = new Dictionary<string, ProviderRules>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in providers.EnumerateObject())
            {
                var aliases = p.Value.TryGetProperty("aliases", out var a) ? a.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!) : new Dictionary<string, string>();
                var strip = p.Value.TryGetProperty("stripConnectionStringKeys", out var s) ? s.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>();
                map[p.Name] = new ProviderRules(p.Value.GetProperty("package").GetString()!, p.Value.TryGetProperty("version", out var v) ? v.GetString() ?? "*" : "*",
                    p.Value.GetProperty("namespace").GetString()!, aliases, strip);
            }
            rules.Providers = map;
        }
        return rules;
    }
}
