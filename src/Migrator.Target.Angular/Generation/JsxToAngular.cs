using System.Text;
using System.Text.RegularExpressions;

namespace Migrator.Target.Angular.Generation;

/// <summary>An inline event handler (`onChange={(v) => { ... }}`) lifted out of the markup: Angular templates cannot hold arrow functions or `await`, so it becomes a function of the code-behind.</summary>
internal sealed record InlineHandler(string Name, bool IsAsync, IReadOnlyList<(string Name, string Type)> Params, string Body);

/// <summary>
/// Turns the generator's neutral view form (JSX-like lines: `&lt;WfButton text="Save" onClick={handler} /&gt;`) into an Angular template.
/// Tags become `wf-*` elements, `name={expr}` becomes `[name]="expr"`, `onX={...}` becomes `(wfX)="..."`, `{cond &amp;&amp; (...)}` becomes `@if`.
/// Components the library does not have become `&lt;wf-unsupported&gt;` placeholders. The input is machine-written and regular, so a small recursive parser is enough.
/// </summary>
internal sealed class JsxToAngular
{
    private readonly ISet<string> _supported;
    private readonly Func<string, int?> _arity;
    private string _src = "";
    private int _pos;
    private readonly List<string> _out = new();
    private int _depth;

    public HashSet<string> Components { get; } = new(StringComparer.Ordinal);
    public List<InlineHandler> Inlines { get; } = new();
    public List<string> Unsupported { get; } = new();

    /// <param name="supported">Component class names the Angular library has.</param>
    /// <param name="arity">Number of parameters of a code-behind function by name (null when unknown): decides whether a bare handler gets the event payload.</param>
    public JsxToAngular(ISet<string> supported, Func<string, int?> arity)
    {
        _supported = supported;
        _arity = arity;
    }

    public List<string> Convert(IEnumerable<string> jsxLines)
    {
        _src = string.Join("\n", jsxLines);
        _pos = 0;
        _out.Clear();
        _depth = 0;
        Children(null);
        return _out.ToList();
    }

    // ------------------------------------------------------------------------------------------------ parsing

    private void Emit(string line) => _out.Add(new string(' ', _depth * 2) + line);

    private bool More => _pos < _src.Length;

    private void SkipSpace()
    {
        while (More && char.IsWhiteSpace(_src[_pos])) _pos++;
    }

    private bool Looking(string text) => string.CompareOrdinal(_src, _pos, text, 0, text.Length) == 0;

    /// <summary>Children up to the closing tag of <paramref name="closing"/> (consumed), or to the end of the text.</summary>
    private void Children(string? closing)
    {
        while (true)
        {
            SkipSpace();
            if (!More) return;
            if (Looking("</"))
            {
                var end = _src.IndexOf('>', _pos);
                _pos = end < 0 ? _src.Length : end + 1;
                return;
            }
            if (_src[_pos] == '<') Element();
            else if (_src[_pos] == '{') Container();
            else _pos++; // stray text: the generator writes none
        }
    }

    private void Container()
    {
        var content = Balanced('{', '}');
        var trimmed = content.Trim();
        if (trimmed.StartsWith("/*", StringComparison.Ordinal) && trimmed.EndsWith("*/", StringComparison.Ordinal))
        {
            Emit($"<!-- {trimmed[2..^2].Trim()} -->");
            return;
        }

        var cond = Regex.Match(trimmed, @"^(?<cond>[\w.$]+)\s*&&\s*\(", RegexOptions.Singleline);
        if (cond.Success && trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            var inner = trimmed[cond.Length..^1];
            Emit($"@if ({cond.Groups["cond"].Value}) {{");
            _depth++;
            var nested = new JsxToAngular(_supported, _arity);
            nested._depth = _depth;
            nested._src = inner;
            nested.Children(null);
            _out.AddRange(nested._out);
            foreach (var c in nested.Components) Components.Add(c);
            Inlines.AddRange(nested.Inlines);
            Unsupported.AddRange(nested.Unsupported);
            _depth--;
            Emit("}");
            return;
        }

        Emit($"<!-- TODO: markup expression not translated: {trimmed.Replace("-->", "--&gt;")} -->");
    }

    /// <summary>The text between a matching pair of brackets starting at the current position (outside strings and comments); the position ends after the closing bracket.</summary>
    private string Balanced(char open, char close)
    {
        var start = _pos + 1;
        var depth = 0;
        for (var i = _pos; i < _src.Length; i++)
        {
            var ch = _src[i];
            if (ch is '"' or '\'' or '`') { i = SkipString(i); continue; }
            if (ch == '/' && i + 1 < _src.Length && _src[i + 1] == '*') { var e = _src.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? _src.Length : e + 1; continue; }
            if (ch == open) depth++;
            else if (ch == close && --depth == 0)
            {
                var text = _src[start..i];
                _pos = i + 1;
                return text;
            }
        }
        var rest = _src[start..];
        _pos = _src.Length;
        return rest;
    }

    private int SkipString(int at)
    {
        var quote = _src[at];
        for (var i = at + 1; i < _src.Length; i++)
        {
            if (_src[i] == '\\') { i++; continue; }
            if (_src[i] == quote) return i;
        }
        return _src.Length;
    }

    private void Element()
    {
        _pos++; // <
        var nameStart = _pos;
        while (More && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] is '.' or '-' or '_')) _pos++;
        var tag = _src[nameStart.._pos];

        var attrs = new List<(string Name, string? Value, bool Braced)>();
        var comments = new List<string>();
        var selfClosing = false;
        while (More)
        {
            SkipSpace();
            if (Looking("/>")) { _pos += 2; selfClosing = true; break; }
            if (Looking(">")) { _pos++; break; }
            if (Looking("/*"))
            {
                var e = _src.IndexOf("*/", _pos + 2, StringComparison.Ordinal);
                comments.Add(_src[(_pos + 2)..(e < 0 ? _src.Length : e)].Trim());
                _pos = e < 0 ? _src.Length : e + 2;
                continue;
            }
            var attrStart = _pos;
            while (More && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] is '-' or '_' or ':')) _pos++;
            if (_pos == attrStart) { _pos++; continue; } // not an attribute: skip a character rather than loop
            var name = _src[attrStart.._pos];
            SkipSpace();
            if (More && _src[_pos] == '=')
            {
                _pos++;
                SkipSpace();
                if (More && _src[_pos] == '"')
                {
                    var end = SkipString(_pos);
                    attrs.Add((name, _src[(_pos + 1)..end], false));
                    _pos = end + 1;
                }
                else if (More && _src[_pos] == '{') attrs.Add((name, Balanced('{', '}'), true));
                else attrs.Add((name, null, false));
            }
            else attrs.Add((name, null, false));
        }

        foreach (var comment in comments) Emit($"<!-- {comment} -->");

        var isComponent = tag.StartsWith("Wf", StringComparison.Ordinal);
        if (isComponent && !_supported.Contains(tag))
        {
            // No component in the Angular library yet: a visible placeholder, never a silent gap. The element's children are dropped with it.
            if (!selfClosing) SkipChildren();
            Unsupported.Add(tag);
            Components.Add("WfUnsupported");
            Emit($"<wf-unsupported type=\"{tag}\" reason=\"Not available in @wfm/ui-angular yet - see MIGRATION_REPORT.md\" />");
            return;
        }

        var element = isComponent ? Kebab(tag) : tag;
        if (isComponent) Components.Add(tag);
        var rendered = new List<string>();
        foreach (var (name, value, braced) in attrs) rendered.AddRange(Attribute(tag, isComponent, name, value, braced));

        // The two panes of a split are told apart by an attribute the template projects on.
        if (tag == "WfSplitPane") rendered.Add(_splitPane++ % 2 == 0 ? "first" : "second");

        var open = rendered.Count == 0 ? $"<{element}" : $"<{element} {string.Join(' ', rendered)}";
        if (selfClosing)
        {
            Emit(isComponent ? open + " />" : open + $"></{element}>");
            return;
        }

        Emit(open + ">");
        _depth++;
        Children(tag);
        _depth--;
        Emit($"</{element}>");
    }

    private int _splitPane;

    /// <summary>Skips the children and the closing tag of an element whose opening tag was just read.</summary>
    private void SkipChildren()
    {
        var level = 1;
        while (More && level > 0)
        {
            if (Looking("</")) { level--; var e = _src.IndexOf('>', _pos); _pos = e < 0 ? _src.Length : e + 1; }
            else if (_src[_pos] == '<')
            {
                // a nested element: skip its tag; a self-closing one does not open a level
                var end = _pos;
                var depth = 0;
                while (end < _src.Length)
                {
                    var ch = _src[end];
                    if (ch is '"' or '\'' or '`') { end = SkipStringAt(end); }
                    else if (ch == '{') depth++;
                    else if (ch == '}') depth--;
                    else if (ch == '>' && depth == 0) break;
                    end++;
                }
                var selfClosing = end > 0 && end < _src.Length && _src[end - 1] == '/';
                _pos = Math.Min(end + 1, _src.Length);
                if (!selfClosing) level++;
            }
            else _pos++;
        }
    }

    private int SkipStringAt(int at) => SkipString(at);

    // ------------------------------------------------------------------------------------------------ attributes

    private IEnumerable<string> Attribute(string tag, bool isComponent, string name, string? value, bool braced)
    {
        if (name == "key") yield break;
        if (name is "className" or "class")
        {
            if (braced) yield return $"[class]=\"{Expression(value!)}\"";
            else if (value is not null) yield return isComponent ? $"className=\"{Escape(value)}\"" : $"class=\"{Escape(value)}\"";
            yield break;
        }

        if (name.Length > 2 && name.StartsWith("on", StringComparison.Ordinal) && char.IsUpper(name[2]))
        {
            yield return Event(tag, name, value ?? "");
            yield break;
        }

        var target = isComponent && name == "id" ? "controlId" : name;
        if (value is null)
        {
            yield return $"[{target}]=\"true\"";
            yield break;
        }
        if (braced)
        {
            yield return $"[{target}]=\"{Expression(value)}\"";
            yield break;
        }
        yield return value.Contains("{{", StringComparison.Ordinal) ? $"[{target}]=\"'{Escape(value).Replace("'", "\\'")}'\"" : $"{target}=\"{Escape(value)}\"";
    }

    private string Event(string tag, string reactName, string expression)
    {
        var angularName = "wf" + reactName[2..];
        var expr = expression.Trim();

        if (Regex.IsMatch(expr, @"^[A-Za-z_$][\w$]*$"))
            return $"({angularName})=\"{expr}({(_arity(expr) is > 0 ? "$event" : "")})\"";

        var arrow = Regex.Match(expr, @"^(?<async>async\s+)?\(?(?<params>[^)=]*?)\)?\s*=>\s*(?<body>.*)$", RegexOptions.Singleline);
        if (!arrow.Success) return $"({angularName})=\"/* not translated: {Escape(expr)} */\"";

        var parameters = arrow.Groups["params"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var body = arrow.Groups["body"].Value.Trim();
        body = body.StartsWith('{') && body.EndsWith('}') ? body[1..^1].Trim() : body.TrimEnd(';') + ";";
        var types = PayloadTypes(tag, reactName, parameters.Count);
        var handler = new InlineHandler($"inline{Inlines.Count + 1}", arrow.Groups["async"].Success, parameters.Select((p, i) => (p, types[i])).ToList(), body);
        Inlines.Add(handler);
        var args = parameters.Count switch { 0 => "", 1 => "$event", _ => "...$event" };
        return $"({angularName})=\"{handler.Name}({args})\"";
    }

    /// <summary>What a control's event carries (the library's output type), so an inline handler's parameters type-check against the template.</summary>
    private static string[] PayloadTypes(string tag, string reactName, int count)
    {
        var known = (tag, reactName) switch
        {
            ("WfTextBox" or "WfDateTimePicker", "onValueChange") => new[] { "string" },
            ("WfNumericUpDown", "onValueChange") => new[] { "number" },
            ("WfComboBox", "onSelectionChange") => new[] { "string" },
            ("WfCheckBox" or "WfRadioButton", "onCheckedChange") => new[] { "boolean" },
            ("WfTabs", "onSelectionChange") => new[] { "number" },
            _ => Array.Empty<string>()
        };
        return Enumerable.Range(0, count).Select(i => i < known.Length ? known[i] : "unknown").ToArray();
    }

    /// <summary>A JavaScript expression inside a double-quoted attribute: string literals use single quotes (an `as` cast does not belong in a template).</summary>
    private static string Expression(string expr)
    {
        var sb = new StringBuilder();
        var text = expr.Trim();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                var j = i + 1;
                var literal = new StringBuilder();
                while (j < text.Length && text[j] != '"')
                {
                    if (text[j] == '\\' && j + 1 < text.Length) { literal.Append(text[j]).Append(text[j + 1]); j += 2; continue; }
                    literal.Append(text[j] == '\'' ? "\\'" : text[j].ToString());
                    j++;
                }
                sb.Append('\'').Append(literal).Append('\'');
                i = j;
            }
            else sb.Append(text[i]);
        }
        return sb.ToString().Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");

    /// <summary>WfDataGrid -> wf-data-grid.</summary>
    internal static string Kebab(string pascal) => Regex.Replace(pascal, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", "-").ToLowerInvariant();
}
