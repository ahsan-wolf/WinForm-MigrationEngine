using System.Text;
using System.Text.RegularExpressions;
using Migrator.Target.Api.Schema;

namespace Migrator.Target.Api.Generation;

/// <summary>
/// Turns the inferred schema into the repository layer of the generated API: one entity, one repository interface and one SQL Server implementation per table
/// (parameterized commands only, column names from a whitelist), a connection factory, a database initializer that runs `schema.sql`, DI registration and
/// REST endpoints `/api/data/&lt;table&gt;`. No application names: everything comes from the schema.
/// </summary>
public static class RepositoryGenerator
{
    public const string Namespace = "Generated.DataAccess";
    public const string DefaultDatabase = "GeneratedApi";

    private static readonly HashSet<string> CsharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue","decimal","default","delegate","do","double","else","enum","event","explicit",
        "extern","false","finally","fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock","long","namespace","new","null","object","operator",
        "out","override","params","private","protected","public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string","struct","switch","this","throw",
        "true","try","typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while"
    };

    public sealed record TableNames(DbTable Table, string Entity, string Repository, string Interface, string Route, string KeyType, Dictionary<string, string> Props);

    public static List<TableNames> Names(DbSchema schema)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<TableNames>();
        foreach (var t in schema.Tables)
        {
            var baseName = Pascal(t.Name);
            var name = baseName;
            for (var i = 2; !used.Add(name); i++) name = baseName + i;
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var propUsed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in t.Columns)
            {
                var p = Pascal(c.Name);
                var candidate = p;
                for (var i = 2; !propUsed.Add(candidate); i++) candidate = p + i;
                props[c.Name] = candidate;
            }
            list.Add(new TableNames(t, name + "Entity", name + "Repository", "I" + name + "Repository", t.Name, ClrType(t.Find(t.KeyColumn!)!.SqlType, false), props));
        }
        return list;
    }

    public static List<(string Path, string Content)> Files(DbSchema schema, string databaseName, IReadOnlyList<string> connectionKeys)
    {
        var names = Names(schema);
        var files = new List<(string, string)>
        {
            ("Data/Abstractions.cs", Abstractions),
            ("Data/SqlRepositoryBase.cs", RepositoryBase),
            ("Data/DatabaseInitializer.cs", Initializer),
            ("Data/DataServices.cs", Services(names, databaseName, connectionKeys)),
            ("Data/DataEndpoints.cs", Endpoints(names)),
        };
        foreach (var n in names)
        {
            files.Add(($"Data/Entities/{n.Entity}.cs", Entity(n)));
            files.Add(($"Data/Repositories/{n.Repository}.cs", Repository(n)));
        }
        return files;
    }

    // ------------------------------------------------------------------------------------------------ names and types

    public static string Pascal(string name)
    {
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var allSameCase = part == part.ToUpperInvariant() || part == part.ToLowerInvariant();
            var p = allSameCase ? part.ToLowerInvariant() : part;
            sb.Append(char.ToUpperInvariant(p[0])).Append(p[1..]);
        }
        var result = sb.Length == 0 ? "Item" : sb.ToString();
        if (char.IsDigit(result[0])) result = "N" + result;
        return CsharpKeywords.Contains(result) ? "@" + result : result;
    }

    public static string ClrType(string sqlType, bool nullable)
    {
        var t = sqlType.ToLowerInvariant();
        var clr = t.StartsWith("bigint") ? "long" : t.StartsWith("smallint") ? "short" : t.StartsWith("int") ? "int" : t.StartsWith("decimal") ? "decimal" : t.StartsWith("datetime") ? "DateTime" : t.StartsWith("bit") ? "bool" : "string";
        return nullable && clr != "string" ? clr + "?" : clr;
    }

    // ------------------------------------------------------------------------------------------------ per table

    private static string Entity(TableNames n)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Generated from the inferred schema of table {n.Table.Name} (types are guesses: see SCHEMA_REPORT.md).");
        sb.AppendLine($"namespace {Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {n.Entity}");
        sb.AppendLine("{");
        foreach (var c in n.Table.Columns)
            sb.AppendLine($"    public {ClrType(c.SqlType, !c.IsKey)} {n.Props[c.Name]} {{ get; set; }}{(c.IsKey ? "" : "   // inferred")}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Repository(TableNames n)
    {
        var t = n.Table;
        var key = t.Find(t.KeyColumn!)!;
        var cols = t.Columns;
        var sb = new StringBuilder();
        sb.AppendLine($"// Generated repository for table {t.Name}: parameterized SQL only.");
        sb.AppendLine("using Microsoft.Data.SqlClient;");
        sb.AppendLine($"namespace {Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public interface {n.Interface} : IRepository<{n.Entity}, {n.KeyType}> {{ }}");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {n.Repository} : SqlRepositoryBase<{n.Entity}, {n.KeyType}>, {n.Interface}");
        sb.AppendLine("{");
        sb.AppendLine($"    public {n.Repository}(IDbConnectionFactory factory) : base(factory) {{ }}");
        sb.AppendLine();
        sb.AppendLine($"    protected override string Table => \"{t.Name}\";");
        sb.AppendLine($"    protected override string KeyColumn => \"{key.Name}\";");
        sb.AppendLine($"    protected override bool KeyIsIdentity => {(key.IsSurrogate ? "true" : "false")};");
        sb.AppendLine($"    protected override string[] Columns => new[] {{ {string.Join(", ", cols.Select(c => $"\"{c.Name}\""))} }};");
        sb.AppendLine();
        sb.AppendLine($"    protected override {n.Entity} Map(SqlDataReader r) => new()");
        sb.AppendLine("    {");
        for (var i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            var clr = ClrType(c.SqlType, false);
            var expr = c.IsKey ? $"r.GetFieldValue<{clr}>({i})" : clr == "string" ? $"r.IsDBNull({i}) ? null : r.GetString({i})" : $"r.IsDBNull({i}) ? null : r.GetFieldValue<{clr}>({i})";
            sb.AppendLine($"        {n.Props[c.Name]} = {expr},");
        }
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine($"    protected override object? ValueOf({n.Entity} e, string column) => column switch");
        sb.AppendLine("    {");
        foreach (var c in cols) sb.AppendLine($"        \"{c.Name}\" => e.{n.Props[c.Name]},");
        sb.AppendLine("        _ => throw new ArgumentException(\"Unknown column: \" + column)");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine($"    protected override {n.KeyType} KeyOf({n.Entity} e) => e.{n.Props[key.Name]};");
        sb.AppendLine($"    protected override void SetKey({n.Entity} e, object key) => e.{n.Props[key.Name]} = ({n.KeyType})Convert.ChangeType(key, typeof({n.KeyType}));");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------------ shared files

    private static string Services(List<TableNames> names, string databaseName, IReadOnlyList<string> connectionKeys)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Data.SqlClient;");
        sb.AppendLine($"namespace {Namespace};");
        sb.AppendLine();
        sb.AppendLine("public static class DataServices");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The connection string comes from configuration/environment only (`ConnectionStrings__Default`). With none, a development default for SQL Server LocalDB is used (Windows login, no secret).</summary>");
        sb.AppendLine("    public static string ResolveConnectionString(IConfiguration configuration) =>");
        sb.AppendLine("        configuration[\"ConnectionStrings:Default\"] is { Length: > 0 } cs ? cs");
        sb.AppendLine($"        : \"Server=(localdb)\\\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;Encrypt=false\";");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The ported data classes read their connection from the old `AppSettings` keys: point those keys at the same database, unless they are set explicitly.</summary>");
        sb.AppendLine("    public static string[] ConnectionSettingKeys => new[] { " + string.Join(", ", connectionKeys.Select(k => "\"" + k + "\"")) + " };");
        sb.AppendLine();
        sb.AppendLine("    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)");
        sb.AppendLine("    {");
        sb.AppendLine("        services.AddSingleton<IDbConnectionFactory>(new DbConnectionFactory(ResolveConnectionString(configuration)));");
        foreach (var n in names) sb.AppendLine($"        services.AddScoped<{n.Interface}, {n.Repository}>();");
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Endpoints(List<TableNames> names)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Generated: REST access to every table through its repository.  GET /, GET /{key}, POST /, PUT /{key}, DELETE /{key}, GET /next-id.");
        sb.AppendLine("// GET /?column=<name>&value=<v> filters on one known column; DELETE /?column=<name>&value=<v> removes the rows of a detail table.");
        sb.AppendLine("using Generated.Api;");
        sb.AppendLine("using Microsoft.Data.SqlClient;");
        sb.AppendLine($"namespace {Namespace};");
        sb.AppendLine();
        sb.AppendLine("public static class DataEndpoints");
        sb.AppendLine("{");
        sb.AppendLine("    private static async Task<IResult> Safe(Func<Task<IResult>> action)");
        sb.AppendLine("    {");
        sb.AppendLine("        try { return await action(); }");
        sb.AppendLine("        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }");
        sb.AppendLine("        catch (SqlException ex) { return Results.Problem(title: \"Database error\", detail: ApiRuntime.Redact(ex.Message), statusCode: 500); }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static void Map(WebApplication app)");
        sb.AppendLine("    {");
        sb.AppendLine("        app.MapGet(\"/api/data\", () => new[] { " + string.Join(", ", names.Select(n => $"\"{n.Route}\"")) + " });");
        for (var i = 0; i < names.Count; i++)
        {
            var n = names[i];
            var g = "g" + i;
            var keyIsInt = n.KeyType == "int";
            sb.AppendLine($"        var {g} = app.MapGroup(\"/api/data/{n.Route}\");");
            sb.AppendLine($"        {g}.MapGet(\"\", ({n.Interface} r, string? column, string? value, CancellationToken ct) => Safe(async () => column is null ? Results.Ok(await r.GetAllAsync(ct)) : Results.Ok(await r.FindAsync(column, value, ct))));");
            sb.AppendLine($"        {g}.MapGet(\"/{{id:{n.KeyType}}}\", ({n.Interface} r, {n.KeyType} id, CancellationToken ct) => Safe(async () => await r.GetByIdAsync(id, ct) is {{ }} e ? Results.Ok(e) : Results.NotFound()));");
            if (keyIsInt && !n.Table.KeyIsSurrogate)
                sb.AppendLine($"        {g}.MapGet(\"/next-id\", ({n.Interface} r, CancellationToken ct) => Safe(async () => Results.Ok(new {{ nextId = await r.NextIdAsync(ct) }})));");
            sb.AppendLine($"        {g}.MapPost(\"\", ({n.Interface} r, {n.Entity} e, CancellationToken ct) => Safe(async () => Results.Ok(await r.AddAsync(e, ct))));");
            sb.AppendLine($"        {g}.MapPut(\"/{{id:{n.KeyType}}}\", ({n.Interface} r, {n.KeyType} id, {n.Entity} e, CancellationToken ct) => Safe(async () => await r.UpdateAsync(id, e, ct) ? Results.Ok(e) : Results.NotFound()));");
            sb.AppendLine($"        {g}.MapDelete(\"/{{id:{n.KeyType}}}\", ({n.Interface} r, {n.KeyType} id, CancellationToken ct) => Safe(async () => await r.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound()));");
            sb.AppendLine($"        {g}.MapDelete(\"\", ({n.Interface} r, string column, string? value, CancellationToken ct) => Safe(async () => Results.Ok(new {{ deleted = await r.DeleteWhereAsync(column, value, ct) }})));");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private const string Abstractions = """
        using System.Data;
        using Microsoft.Data.SqlClient;

        namespace Generated.DataAccess;

        public interface IDbConnectionFactory
        {
            string ConnectionString { get; }
            Task<SqlConnection> OpenAsync(CancellationToken ct = default);
        }

        public sealed class DbConnectionFactory : IDbConnectionFactory
        {
            public DbConnectionFactory(string connectionString) => ConnectionString = connectionString;
            public string ConnectionString { get; }

            public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
            {
                var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync(ct);
                return connection;
            }
        }

        /// <summary>Data access for one table. Column names given by callers are checked against the table's own column list; values are always parameters.</summary>
        public interface IRepository<TEntity, TKey> where TEntity : class
        {
            Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default);
            Task<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default);
            Task<IReadOnlyList<TEntity>> FindAsync(string column, object? value, CancellationToken ct = default);
            Task<TEntity> AddAsync(TEntity entity, CancellationToken ct = default);
            Task<bool> UpdateAsync(TKey id, TEntity entity, CancellationToken ct = default);
            Task<bool> DeleteAsync(TKey id, CancellationToken ct = default);
            Task<int> DeleteWhereAsync(string column, object? value, CancellationToken ct = default);
            Task<int> NextIdAsync(CancellationToken ct = default);
        }
        """;

    private const string RepositoryBase = """
        using Microsoft.Data.SqlClient;

        namespace Generated.DataAccess;

        /// <summary>The SQL of every repository lives here once; a generated subclass supplies the table, its columns and the entity mapping.</summary>
        public abstract class SqlRepositoryBase<TEntity, TKey> : IRepository<TEntity, TKey> where TEntity : class
        {
            private readonly IDbConnectionFactory _factory;
            protected SqlRepositoryBase(IDbConnectionFactory factory) => _factory = factory;

            protected abstract string Table { get; }
            protected abstract string KeyColumn { get; }
            protected abstract bool KeyIsIdentity { get; }
            protected abstract string[] Columns { get; }
            protected abstract TEntity Map(SqlDataReader reader);
            protected abstract object? ValueOf(TEntity entity, string column);
            protected abstract TKey KeyOf(TEntity entity);
            protected abstract void SetKey(TEntity entity, object key);

            private string Select => "SELECT " + string.Join(", ", Columns.Select(Quote)) + " FROM dbo." + Quote(Table);
            private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

            private string Known(string column) =>
                Columns.FirstOrDefault(c => c.Equals(column, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Unknown column: " + column);

            private async Task<List<TEntity>> Query(string sql, Action<SqlCommand>? bind, CancellationToken ct)
            {
                await using var connection = await _factory.OpenAsync(ct);
                await using var command = new SqlCommand(sql, connection);
                bind?.Invoke(command);
                var list = new List<TEntity>();
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) list.Add(Map(reader));
                return list;
            }

            private async Task<int> NonQuery(string sql, Action<SqlCommand> bind, CancellationToken ct)
            {
                await using var connection = await _factory.OpenAsync(ct);
                await using var command = new SqlCommand(sql, connection);
                bind(command);
                return await command.ExecuteNonQueryAsync(ct);
            }

            public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default) => await Query(Select, null, ct);

            public async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default) =>
                (await Query(Select + " WHERE " + Quote(KeyColumn) + " = @key", c => c.Parameters.AddWithValue("@key", id!), ct)).FirstOrDefault();

            public async Task<IReadOnlyList<TEntity>> FindAsync(string column, object? value, CancellationToken ct = default)
            {
                var known = Known(column);
                return await Query(Select + " WHERE " + Quote(known) + " = @value", c => c.Parameters.AddWithValue("@value", value ?? DBNull.Value), ct);
            }

            public async Task<TEntity> AddAsync(TEntity entity, CancellationToken ct = default)
            {
                var columns = Columns.Where(c => !(KeyIsIdentity && c == KeyColumn)).ToArray();
                var sql = "INSERT INTO dbo." + Quote(Table) + " (" + string.Join(", ", columns.Select(Quote)) + ")"
                    + (KeyIsIdentity ? " OUTPUT INSERTED." + Quote(KeyColumn) : "")
                    + " VALUES (" + string.Join(", ", columns.Select((_, i) => "@p" + i)) + ")";
                await using var connection = await _factory.OpenAsync(ct);
                await using var command = new SqlCommand(sql, connection);
                for (var i = 0; i < columns.Length; i++) command.Parameters.AddWithValue("@p" + i, ValueOf(entity, columns[i]) ?? DBNull.Value);
                if (KeyIsIdentity) SetKey(entity, (await command.ExecuteScalarAsync(ct))!);
                else await command.ExecuteNonQueryAsync(ct);
                return entity;
            }

            public async Task<bool> UpdateAsync(TKey id, TEntity entity, CancellationToken ct = default)
            {
                var columns = Columns.Where(c => c != KeyColumn).ToArray();
                if (columns.Length == 0) return await GetByIdAsync(id, ct) is not null;
                var sql = "UPDATE dbo." + Quote(Table) + " SET " + string.Join(", ", columns.Select((c, i) => Quote(c) + " = @p" + i)) + " WHERE " + Quote(KeyColumn) + " = @key";
                return await NonQuery(sql, c =>
                {
                    for (var i = 0; i < columns.Length; i++) c.Parameters.AddWithValue("@p" + i, ValueOf(entity, columns[i]) ?? DBNull.Value);
                    c.Parameters.AddWithValue("@key", id!);
                }, ct) > 0;
            }

            public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default) =>
                await NonQuery("DELETE FROM dbo." + Quote(Table) + " WHERE " + Quote(KeyColumn) + " = @key", c => c.Parameters.AddWithValue("@key", id!), ct) > 0;

            public async Task<int> DeleteWhereAsync(string column, object? value, CancellationToken ct = default)
            {
                var known = Known(column);
                return await NonQuery("DELETE FROM dbo." + Quote(Table) + " WHERE " + Quote(known) + " = @value", c => c.Parameters.AddWithValue("@value", value ?? DBNull.Value), ct);
            }

            /// <summary>The next key for tables whose code computed `max(key) + 1` itself (an empty table gives 1, where the original gave NULL).</summary>
            public async Task<int> NextIdAsync(CancellationToken ct = default)
            {
                await using var connection = await _factory.OpenAsync(ct);
                await using var command = new SqlCommand("SELECT ISNULL(MAX(" + Quote(KeyColumn) + "), 0) + 1 FROM dbo." + Quote(Table), connection);
                return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
            }
        }
        """;

    private const string Initializer = """
        using Microsoft.Data.SqlClient;

        namespace Generated.DataAccess;

        /// <summary>
        /// Creates the database and the inferred tables from `Data/schema.sql` when asked to: `Database:AutoCreate` (default true for LocalDB, false for any other server).
        /// Idempotent: tables are created only when missing.
        /// </summary>
        public static class DatabaseInitializer
        {
            public static async Task InitializeAsync(IConfiguration configuration, IDbConnectionFactory factory, ILogger logger)
            {
                var builder = new SqlConnectionStringBuilder(factory.ConnectionString);
                var auto = configuration.GetValue<bool?>("Database:AutoCreate") ?? builder.DataSource.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase);
                if (!auto) return;

                var database = builder.InitialCatalog;
                if (database.Length == 0) throw new InvalidOperationException("The connection string names no database (Initial Catalog / Database).");
                var master = new SqlConnectionStringBuilder(factory.ConnectionString) { InitialCatalog = "master" };
                await using (var connection = new SqlConnection(master.ConnectionString))
                {
                    await connection.OpenAsync();
                    await using var create = new SqlCommand("IF DB_ID(@name) IS NULL BEGIN DECLARE @sql nvarchar(400) = N'CREATE DATABASE ' + QUOTENAME(@name); EXEC(@sql); END", connection);
                    create.Parameters.AddWithValue("@name", database);
                    await create.ExecuteNonQueryAsync();
                }

                var script = Path.Combine(AppContext.BaseDirectory, "Data", "schema.sql");
                if (!File.Exists(script)) { logger.LogWarning("Data/schema.sql not found next to the application: tables not created."); return; }
                var batches = System.Text.RegularExpressions.Regex.Split(await File.ReadAllTextAsync(script), @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    .Where(b => b.Trim().Length > 0);
                await using var db = await factory.OpenAsync();
                foreach (var batch in batches)
                {
                    await using var command = new SqlCommand(batch, db);
                    await command.ExecuteNonQueryAsync();
                }
                logger.LogInformation("Database '{Database}' is ready.", database);
            }
        }
        """;
}
