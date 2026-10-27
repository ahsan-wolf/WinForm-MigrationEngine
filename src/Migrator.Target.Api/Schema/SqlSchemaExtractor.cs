using System.Text;
using System.Text.RegularExpressions;
using Migrator.Engine.Support;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.Api.Schema;

/// <summary>
/// Recovers the database structure from the SQL text in the data-access code: tables (FROM / JOIN / INTO / UPDATE / DELETE), columns (INSERT lists, SET lists, SELECT lists,
/// WHERE and ON comparisons), keys (`select max(col) + 1 from T`) and join relations. SQL is rebuilt from the C# string literals of a statement (string.Concat, `+`,
/// `object[]` fragments) with every non-literal value kept as a placeholder, so the C# type of a written value (a field, a parameter, a local) types the column.
/// Nothing is executed and no database is read; everything is marked as inferred in the report.
/// </summary>
public static class SqlSchemaExtractor
{
    private static readonly Regex StringLiteral = new(@"""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);
    private static readonly Regex SqlShape = new(@"\b(select\b[\s\S]*\bfrom\b|insert\s+into\b|update\s+\S+\s+set\b|delete\s+(from\s+)?\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "and", "or", "not", "null", "is", "in", "like", "between", "as", "on", "join", "inner", "left", "right", "outer", "full", "cross", "group", "by", "order",
        "having", "union", "all", "distinct", "top", "set", "values", "insert", "into", "update", "delete", "case", "when", "then", "else", "end", "asc", "desc", "exists", "count", "sum",
        "max", "min", "avg", "isnull", "getdate", "convert", "cast", "iif", "now", "true", "false", "int", "varchar", "nvarchar", "datetime", "decimal", "float", "date"
    };
    private static readonly HashSet<string> AliasStoppers = new(StringComparer.OrdinalIgnoreCase)
    {
        "where", "inner", "left", "right", "outer", "full", "cross", "join", "on", "group", "order", "having", "union", "set", "values", "select", "and", "or"
    };

    public static DbSchema Extract(IEnumerable<SupportClassIr> classes, SchemaInferRules rules)
    {
        var list = classes.Where(c => !c.External).ToList();
        var fieldTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in list)
            foreach (var m in c.Members.Where(m => m.Kind is MemberKind.Field or MemberKind.Property && !string.IsNullOrEmpty(m.Type)))
                fieldTypes.TryAdd(m.Name, m.Type!);

        var state = new State(rules);
        foreach (var cls in list)
            foreach (var m in cls.Members.Where(m => m.Kind == MemberKind.Method && !string.IsNullOrEmpty(m.Body)))
            {
                var locals = LocalTypes(m);
                foreach (var stmt in Statements(m.Body!))
                {
                    if (!SqlShape.IsMatch(stmt.Text)) continue;
                    new StatementReader(state, fieldTypes, locals, stmt, $"{cls.Name}.{m.Name}").Read();
                }
            }
        return state.Finish();
    }

    // ------------------------------------------------------------------------------------------------ SQL text from C#

    private sealed record Stmt(string Text, List<string> Exprs);

    private static Dictionary<string, string> LocalTypes(MemberIr method)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in method.Parameters) map[p.Name] = p.Type;
        foreach (Match d in Regex.Matches(method.Body ?? "", @"\b(int|long|short|double|float|decimal|string|bool|DateTime)\s+(\w+)\s*(=|;|,)"))
            map.TryAdd(d.Groups[2].Value, d.Groups[1].Value);
        return map;
    }

    /// <summary>Splits a method body at `;` outside string literals and rebuilds each piece that holds literals as SQL text.</summary>
    private static IEnumerable<Stmt> Statements(string body)
    {
        var start = 0;
        var inString = false;
        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            if (inString) { if (ch == '\\') i++; else if (ch == '"') inString = false; continue; }
            if (ch == '"') { inString = true; continue; }
            if (ch == ';') { if (Rebuild(body[start..i]) is { } s) yield return s; start = i + 1; }
        }
        if (start < body.Length && Rebuild(body[start..]) is { } last) yield return last;
    }

    private static Stmt? Rebuild(string segment)
    {
        var literals = StringLiteral.Matches(segment);
        if (literals.Count == 0) return null;
        var text = new StringBuilder();
        var exprs = new List<string>();
        for (var i = 0; i < literals.Count; i++)
        {
            text.Append(Unescape(literals[i].Value));
            var from = literals[i].Index + literals[i].Length;
            var to = i + 1 < literals.Count ? literals[i + 1].Index : segment.Length;
            foreach (var expr in GapExpressions(segment[from..to]))
            {
                text.Append('\u0001').Append(exprs.Count).Append('\u0002');
                exprs.Add(expr);
            }
        }
        return new Stmt(Regex.Replace(text.ToString(), @"\s+", " ").Trim(), exprs);
    }

    private static string Unescape(string literal) =>
        literal[1..^1].Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\n", " ").Replace("\\r", " ").Replace("\\t", " ").Replace("\\\\", "\\");

    /// <summary>The value expressions between two literals: split at depth-0 `,` and `+`; a closing bracket that has no opener ends the argument list.</summary>
    private static IEnumerable<string> GapExpressions(string gap)
    {
        var depth = 0;
        var current = new StringBuilder();
        var parts = new List<string>();
        var stop = false;
        foreach (var ch in gap)
        {
            if (ch is '(' or '[' or '{') depth++;
            else if (ch is ')' or ']' or '}')
            {
                if (depth == 0) { stop = true; break; }
                depth--;
            }
            if (depth == 0 && ch is ',' or '+') { parts.Add(current.ToString()); current.Clear(); continue; }
            current.Append(ch);
        }
        parts.Add(current.ToString());
        _ = stop;
        foreach (var p in parts)
        {
            var e = p.Trim();
            if (e.Length == 0 || e.StartsWith("new ", StringComparison.Ordinal)) continue;
            yield return e;
        }
    }

    // ------------------------------------------------------------------------------------------------ accumulated state

    private sealed class State
    {
        public readonly SchemaInferRules Rules;
        public readonly Dictionary<string, DbTable> Tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string Type, string Reason, bool IsString)> _evidence = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> DeletedWholly = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> FilledFromSelect = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> MaxKeys = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<(string T1, string C1, string T2, string C2, string Where)> Pairs = new();

        public State(SchemaInferRules rules) => Rules = rules;

        public DbTable? Table(string name, string usedBy)
        {
            if (name.Length == 0 || char.IsDigit(name[0]) || Keywords.Contains(name) || Rules.IgnoreTables.Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
            if (!Tables.TryGetValue(name, out var t)) Tables[name] = t = new DbTable { Name = name };
            t.UsedBy.Add(usedBy);
            return t;
        }

        public void Column(DbTable t, string column)
        {
            if (column.Length == 0 || char.IsDigit(column[0]) || Keywords.Contains(column) || column.StartsWith("Expr", StringComparison.Ordinal) && column.Length <= 6) return;
            if (t.Find(column) is null) t.Columns.Add(new DbColumn { Name = column });
        }

        public void AddEvidence(DbTable t, string column, string sqlType, string reason)
        {
            var key = t.Name + "." + column;
            var isString = sqlType.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase);
            // A non-string observation wins over a string one: this style of code writes numbers and dates through string fields as often as not.
            if (_evidence.TryGetValue(key, out var old) && !(old.IsString && !isString)) return;
            _evidence[key] = (sqlType, reason, isString);
        }

        public DbSchema Finish()
        {
            var schema = new DbSchema();
            foreach (var t in Tables.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (MaxKeys.TryGetValue(t.Name, out var keyColumn))
                {
                    Column(t, keyColumn);
                    var kc = t.Find(keyColumn)!;
                    kc.IsKey = true;
                    kc.SqlType = "int"; kc.TypeReason = "key: select max(" + keyColumn + ") + 1"; kc.TypeIsEvidence = true;
                    t.KeyColumn = kc.Name;
                    t.KeyReason = $"`select max({keyColumn}) + 1` computes the next value";
                }
                foreach (var c in t.Columns.Where(c => !c.IsKey))
                {
                    if (_evidence.TryGetValue(t.Name + "." + c.Name, out var e)) { c.SqlType = e.Type; c.TypeReason = e.Reason; c.TypeIsEvidence = true; }
                    else if (Rules.HintFor(c.Name) is { } hint) { c.SqlType = hint; c.TypeReason = "name hint"; }
                    else { c.SqlType = Rules.DefaultType; c.TypeReason = "default"; }
                }
                if (t.KeyColumn is null)
                {
                    var name = Rules.SurrogateKeyName;
                    while (t.Find(name) is not null) name += "_";
                    t.Columns.Insert(0, new DbColumn { Name = name, SqlType = "int", TypeReason = "surrogate", TypeIsEvidence = false, IsKey = true, IsSurrogate = true });
                    t.KeyColumn = name;
                    t.KeyIsSurrogate = true;
                    t.KeyReason = "no key evidence in the code: identity column added";
                }
                t.LooksLikeWorkTable = DeletedWholly.Contains(t.Name) && FilledFromSelect.Contains(t.Name);
                schema.Tables.Add(t);
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (t1, c1, t2, c2, where) in Pairs)
            {
                if (!Tables.TryGetValue(t1, out var a) || !Tables.TryGetValue(t2, out var b)) continue;
                var aKey = a.KeyColumn is not null && !a.KeyIsSurrogate && a.KeyColumn.Equals(c1, StringComparison.OrdinalIgnoreCase);
                var bKey = b.KeyColumn is not null && !b.KeyIsSurrogate && b.KeyColumn.Equals(c2, StringComparison.OrdinalIgnoreCase);
                var rel = bKey || !aKey ? new DbRelation(a.Name, c1, b.Name, c2, where) : new DbRelation(b.Name, c2, a.Name, c1, where);
                if (seen.Add($"{rel.Table}.{rel.Column}>{rel.ReferencedTable}.{rel.ReferencedColumn}")) schema.Relations.Add(rel);
            }
            return schema;
        }
    }

    // ------------------------------------------------------------------------------------------------ one statement

    private sealed class StatementReader
    {
        private readonly State _s;
        private readonly Dictionary<string, string> _fields;
        private readonly Dictionary<string, string> _locals;
        private readonly Stmt _stmt;
        private readonly string _by;
        private readonly Dictionary<string, DbTable> _aliases = new(StringComparer.OrdinalIgnoreCase);

        public StatementReader(State s, Dictionary<string, string> fields, Dictionary<string, string> locals, Stmt stmt, string by)
        { _s = s; _fields = fields; _locals = locals; _stmt = stmt; _by = by; }

        public void Read()
        {
            var text = _stmt.Text.Replace("[", "").Replace("]", "");
            var insert = Regex.Match(text, @"\binsert\s+into\s+(?:\w+\.)?(\w+)\s*(?:\(([^)]*)\))?\s*(?:(values)\s*\((.*)\)?|(select\b.*))?", RegexOptions.IgnoreCase);
            if (insert.Success) ReadInsert(insert);
            var update = Regex.Match(text, @"\bupdate\s+(?:\w+\.)?(\w+)\s+set\s+(.*?)(?:\s+where\s+(.*))?$", RegexOptions.IgnoreCase);
            if (update.Success) ReadUpdate(update);
            var delete = Regex.Match(text, @"\bdelete\s+(?:from\s+)?(?:\w+\.)?(\w+)(?:\s+where\s+(.*))?$", RegexOptions.IgnoreCase);
            if (delete.Success && !insert.Success) ReadDelete(delete);
            var select = Regex.Match(text, @"\bselect\b(.*)", RegexOptions.IgnoreCase);
            if (select.Success && !update.Success && !delete.Success) ReadSelect(select.Groups[1].Value, insert);
        }

        private void ReadInsert(Match m)
        {
            var table = _s.Table(m.Groups[1].Value, _by);
            if (table is null) return;
            var cols = m.Groups[2].Success ? m.Groups[2].Value.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList() : new List<string>();
            foreach (var c in cols) _s.Column(table, c);
            if (m.Groups[5].Success) _s.FilledFromSelect.Add(table.Name);
            if (!m.Groups[3].Success) return;
            var values = SplitTopLevel(m.Groups[4].Value);
            for (var i = 0; i < Math.Min(cols.Count, values.Count); i++)
                if (TypeOfValue(values[i]) is { } t) _s.AddEvidence(table, cols[i], t, "value written by " + _by);
        }

        private void ReadUpdate(Match m)
        {
            var table = _s.Table(m.Groups[1].Value, _by);
            if (table is null) return;
            _aliases[table.Name] = table;
            foreach (var pair in SplitTopLevel(m.Groups[2].Value))
            {
                var eq = Regex.Match(pair, @"^\s*(\w+)\s*=\s*(.*)$");
                if (!eq.Success) continue;
                _s.Column(table, eq.Groups[1].Value);
                if (TypeOfValue(eq.Groups[2].Value) is { } t) _s.AddEvidence(table, eq.Groups[1].Value, t, "value written by " + _by);
            }
            if (m.Groups[3].Success) ReadConditions(m.Groups[3].Value, table);
        }

        private void ReadDelete(Match m)
        {
            var table = _s.Table(m.Groups[1].Value, _by);
            if (table is null) return;
            _aliases[table.Name] = table;
            if (m.Groups[2].Success) ReadConditions(m.Groups[2].Value, table);
            else _s.DeletedWholly.Add(table.Name);
        }

        private void ReadSelect(string afterSelect, Match insert)
        {
            var fromIdx = Regex.Match(afterSelect, @"\bfrom\b", RegexOptions.IgnoreCase);
            if (!fromIdx.Success) return;
            var list = afterSelect[..fromIdx.Index];
            var rest = afterSelect[fromIdx.Index..];
            var tables = new List<DbTable>();
            foreach (Match t in Regex.Matches(rest, @"\b(?:from|join)\s+(?:\w+\.)?(\w+)(?:\s+(?:as\s+)?(\w+))?", RegexOptions.IgnoreCase))
            {
                var table = _s.Table(t.Groups[1].Value, _by);
                if (table is null) continue;
                tables.Add(table);
                _aliases[table.Name] = table;
                if (t.Groups[2].Success && !AliasStoppers.Contains(t.Groups[2].Value)) _aliases[t.Groups[2].Value] = table;
            }
            if (tables.Count == 0) return;

            var max = Regex.Match(list, @"max\(\s*(?:\w+\.)?(\w+)\s*\)\s*\+\s*1", RegexOptions.IgnoreCase);
            if (max.Success && tables.Count == 1) _s.MaxKeys.TryAdd(tables[0].Name, max.Groups[1].Value);

            var only = tables.Count == 1 ? tables[0] : null;
            var cleaned = Regex.Replace(list, @"'[^']*'|\u0001\d+\u0002|\b(?:distinct|top\s+\d+)\b|\bas\s+\w+", " ", RegexOptions.IgnoreCase);
            foreach (Match id in Regex.Matches(cleaned, @"(?:(\w+)\.)?\b([A-Za-z_]\w*)\b(?!\s*\()"))
            {
                if (Keywords.Contains(id.Groups[2].Value) && !id.Groups[1].Success) continue;
                Attribute(id.Groups[1].Success ? id.Groups[1].Value : null, id.Groups[2].Value, only);
            }

            var where = Regex.Match(rest, @"\bwhere\b(.*)$", RegexOptions.IgnoreCase);
            if (where.Success) ReadConditions(where.Groups[1].Value, only);
            foreach (Match on in Regex.Matches(rest, @"(\w+)\.(\w+)\s*=\s*(\w+)\.(\w+)"))
                Pair(on.Groups[1].Value, on.Groups[2].Value, on.Groups[3].Value, on.Groups[4].Value);
        }

        /// <summary>Comparisons in a WHERE: columns exist, and a placeholder compared to a column types it.</summary>
        private void ReadConditions(string where, DbTable? only)
        {
            if (only is null && _aliases.Count == 1) only = _aliases.Values.First();
            foreach (Match c in Regex.Matches(where, @"(?:(\w+)\.)?\b([A-Za-z_]\w*)\b\s*(=|<>|<=|>=|<|>|\blike\b)\s*('?)(\u0001(\d+)\u0002)?", RegexOptions.IgnoreCase))
            {
                if (Keywords.Contains(c.Groups[2].Value)) continue;
                var table = Attribute(c.Groups[1].Success ? c.Groups[1].Value : null, c.Groups[2].Value, only);
                if (table is not null && c.Groups[5].Success && int.TryParse(c.Groups[6].Value, out var idx) && TypeOfExpr(idx) is { } t)
                    _s.AddEvidence(table, c.Groups[2].Value, t, "compared in " + _by);
            }
            foreach (Match on in Regex.Matches(where, @"(\w+)\.(\w+)\s*=\s*(\w+)\.(\w+)"))
                Pair(on.Groups[1].Value, on.Groups[2].Value, on.Groups[3].Value, on.Groups[4].Value);
        }

        private DbTable? Attribute(string? qualifier, string column, DbTable? only)
        {
            DbTable? table = null;
            if (qualifier is not null) _aliases.TryGetValue(qualifier, out table);
            else table = only;
            if (table is not null) _s.Column(table, column);
            return table;
        }

        private void Pair(string q1, string c1, string q2, string c2)
        {
            if (_aliases.TryGetValue(q1, out var a) && _aliases.TryGetValue(q2, out var b) && !ReferenceEquals(a, b))
            {
                _s.Column(a, c1); _s.Column(b, c2);
                _s.Pairs.Add((a.Name, c1, b.Name, c2, _by));
            }
        }

        // -------------------------------------------------------------------------------------------- value types

        private string? TypeOfValue(string value)
        {
            value = value.Trim();
            var ph = Regex.Match(value, @"^'?\u0001(\d+)\u0002'?$");
            if (ph.Success) return TypeOfExpr(int.Parse(ph.Groups[1].Value));
            if (Regex.IsMatch(value, @"^-?\d+$")) return null;          // a bare 0 says nothing (counters, defaults)
            if (Regex.IsMatch(value, @"^-?\d+\.\d+$")) return "decimal(18,2)";
            return null;
        }

        private string? TypeOfExpr(int index)
        {
            if (index >= _stmt.Exprs.Count) return null;
            var expr = _stmt.Exprs[index];
            if (Regex.IsMatch(expr, @"^(int|Int32)\.Parse|Convert\.ToInt32|^\(int\)")) return Map("int");
            if (Regex.IsMatch(expr, @"^(double|decimal|float)\.Parse|Convert\.To(Double|Decimal)|^\((double|decimal|float)\)")) return Map("decimal");
            if (Regex.IsMatch(expr, @"Convert\.ToDateTime|DateTime\.(Now|Today|Parse)")) return Map("DateTime");
            var simple = Regex.Replace(expr, @"\.(ToString|ToShortDateString|ToLongDateString|Trim|ToUpper|ToLower)\([^)]*\)$", "");
            if (_locals.TryGetValue(simple, out var local)) return Map(local);
            var last = Regex.Match(simple, @"(\w+)\s*$");
            if (last.Success && _locals.TryGetValue(last.Groups[1].Value, out var l2) && !simple.Contains('.')) return Map(l2);
            if (last.Success && _fields.TryGetValue(last.Groups[1].Value, out var f)) return Map(f);
            return null;
        }

        private string? Map(string csharpType)
        {
            var t = csharpType.Trim().TrimEnd('?');
            if (t.EndsWith("]", StringComparison.Ordinal) || t.Contains('<')) return null;
            return _s.Rules.CsharpTypes.TryGetValue(t, out var sql) ? sql : null;
        }

        /// <summary>Top-level comma split of a VALUES or SET list: commas inside quotes or brackets do not split.</summary>
        private static List<string> SplitTopLevel(string text)
        {
            var parts = new List<string>();
            var depth = 0;
            var quote = false;
            var current = new StringBuilder();
            foreach (var ch in text)
            {
                if (ch == '\'') quote = !quote;
                else if (!quote && ch == '(') depth++;
                else if (!quote && ch == ')') { if (depth == 0) break; depth--; }
                if (!quote && depth == 0 && ch == ',') { parts.Add(current.ToString()); current.Clear(); continue; }
                current.Append(ch);
            }
            if (current.Length > 0) parts.Add(current.ToString());
            return parts;
        }
    }
}
