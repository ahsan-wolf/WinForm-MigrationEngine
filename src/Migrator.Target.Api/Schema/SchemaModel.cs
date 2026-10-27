using System.Text.Json;
using System.Text.RegularExpressions;

namespace Migrator.Target.Api.Schema;

/// <summary>The database structure recovered from the SQL text of the application's data-access code. Everything here is inferred, never read from a database.</summary>
public sealed class DbSchema
{
    public List<DbTable> Tables { get; } = new();
    public List<DbRelation> Relations { get; } = new();
    public List<string> Notes { get; } = new();
}

public sealed class DbTable
{
    public string Name { get; init; } = "";
    public List<DbColumn> Columns { get; } = new();

    /// <summary>The key column(s). Empty before keys are decided; a table with no evidence of a key gets a surrogate identity column.</summary>
    public string? KeyColumn { get; set; }
    public bool KeyIsSurrogate { get; set; }
    public string KeyReason { get; set; } = "";

    /// <summary>Methods whose SQL mentions the table (class.method), for the report.</summary>
    public SortedSet<string> UsedBy { get; } = new(StringComparer.Ordinal);

    /// <summary>The table is only ever deleted from in full and refilled by INSERT ... SELECT: a report work table, not data.</summary>
    public bool LooksLikeWorkTable { get; set; }

    public DbColumn? Find(string column) => Columns.FirstOrDefault(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
}

public sealed class DbColumn
{
    public string Name { get; init; } = "";
    public string SqlType { get; set; } = "nvarchar(255)";

    /// <summary>Why the type was chosen ("C# int written by X.Save", "name hint", "default").</summary>
    public string TypeReason { get; set; } = "default";
    public bool TypeIsEvidence { get; set; }
    public bool IsKey { get; set; }
    public bool IsSurrogate { get; set; }
}

public sealed record DbRelation(string Table, string Column, string ReferencedTable, string ReferencedColumn, string Where);

/// <summary>Inference rules (data file `rules/schema-infer.json`).</summary>
public sealed class SchemaInferRules
{
    public string DefaultType { get; set; } = "nvarchar(255)";
    public List<TypeHint> ColumnTypeHints { get; set; } = new();
    public Dictionary<string, string> CsharpTypes { get; set; } = new();
    public List<string> IgnoreTables { get; set; } = new();
    public string SurrogateKeyName { get; set; } = "RowId";

    public sealed class TypeHint
    {
        public string Pattern { get; set; } = "";
        public string Type { get; set; } = "";
    }

    public static SchemaInferRules Load(string path)
    {
        if (!File.Exists(path)) return new SchemaInferRules();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        return JsonSerializer.Deserialize<SchemaInferRules>(File.ReadAllText(path), options) ?? new SchemaInferRules();
    }

    public string? HintFor(string column)
    {
        foreach (var h in ColumnTypeHints)
            if (Regex.IsMatch(column, "^(" + h.Pattern + ")$", RegexOptions.IgnoreCase)) return h.Type;
        return null;
    }
}
