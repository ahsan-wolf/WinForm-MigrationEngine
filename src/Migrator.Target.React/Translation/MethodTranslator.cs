using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Target.React.Generation;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.React.Translation;

/// <summary>
/// Syntax-directed C# -> TypeScript for one method body. Deterministic and conservative: any statement it cannot translate faithfully
/// stays as a commented original plus a TODO (CLAUDE.md §6) and never aborts the method. Names follow the page conventions:
/// controls are `vm.&lt;id&gt;.&lt;prop&gt;`, fields are `f.&lt;name&gt;`, dialogs are `dialog`, the service is `&lt;instance&gt;.&lt;Facade&gt;.&lt;Method&gt;`.
/// </summary>
internal sealed partial class MethodTranslator
{
    private static readonly HashSet<string> ModelProps = new(StringComparer.Ordinal) { "Text", "Enabled", "Visible", "Checked", "Value", "ReadOnly" };

    /// <summary>Appearance-only members: no behaviour to migrate. Assignments are dropped with a comment (layout comes from Dock/Location facts).</summary>
    private static readonly HashSet<string> VisualProps = new(StringComparer.Ordinal)
    {
        "Anchor", "Dock", "Size", "Location", "Font", "ForeColor", "BackColor", "BackgroundImage", "BackgroundImageLayout", "Cursor", "Icon",
        "TabIndex", "TabStop", "Padding", "Margin", "Width", "Height", "AutoSize", "Image", "ImageIndex", "ImageAlign", "TextAlign", "FlatStyle",
        "BorderStyle", "MinimumSize", "MaximumSize", "StartPosition", "WindowState", "FormBorderStyle", "ShowInTaskbar", "TopMost", "Opacity",
        "ControlBox", "MaximizeBox", "MinimizeBox", "AutoScroll", "ToolTipText", "ClientSize", "Left", "Top", "Right", "Bottom"
    };

    private static readonly JsonSerializerOptions JsonOut = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly PageScope _scope;
    private readonly MemberIr? _method;
    private readonly bool _constantContext;
    private readonly HashSet<string> _locals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _formLocals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _confirmLocals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _catchVars = new(StringComparer.Ordinal);

    /// <summary>Locals whose declaration could not be translated: anything depending on them falls back too (no dangling references).</summary>
    private readonly HashSet<string> _poisoned = new(StringComparer.Ordinal);

    /// <summary>Locals declared with a type that has no TypeScript counterpart (ListViewItem, TreeNode, StringBuilder, ...): any member access falls back.</summary>
    private readonly HashSet<string> _opaqueLocals = new(StringComparer.Ordinal);

    /// <summary>Locals assigned (or incremented, or passed by ref/out) after their declaration; the rest become `const`.</summary>
    private readonly HashSet<string> _reassigned = new(StringComparer.Ordinal);
    private readonly List<string> _after = new();
    private string? _pendingResult;

    public List<string> Reasons { get; } = new();
    public int Todos { get; private set; }

    public MethodTranslator(PageScope scope, MemberIr? method, bool constantContext = false)
    {
        _scope = scope;
        _method = method;
        _constantContext = constantContext;
    }

    // ------------------------------------------------------------------------------------------------- entry points

    /// <summary>Translates the method body into statement lines. top = top-level statements; fallbacks = how many of them fell back entirely.</summary>
    public List<string> TranslateBody(out int top, out int fallbacks)
    {
        top = 0;
        fallbacks = 0;
        var body = _method?.Body;
        if (string.IsNullOrWhiteSpace(body)) return new List<string>();

        var tree = CSharpSyntaxTree.ParseText("class __W { void __M() " + body + " }");
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var statements = declaration.Body?.Statements.ToList()
                         ?? (declaration.ExpressionBody is { } eb ? new List<StatementSyntax> { SyntaxFactory.ExpressionStatement(eb.Expression) } : new List<StatementSyntax>());

        foreach (var p in _method!.Parameters) _locals.Add(p.Name);
        CollectLocals(declaration);

        var lines = new List<string>();
        foreach (var s in statements)
        {
            var translated = Stmt(s);
            top++;
            if (translated.Count > 0 && translated.All(l => l.StartsWith("//", StringComparison.Ordinal)) && translated[0].StartsWith("// TODO", StringComparison.Ordinal))
                fallbacks++;
            lines.AddRange(translated);
        }
        return lines;
    }

    /// <summary>Translates a stand-alone constant expression (field initializer, parameter default). Throws <see cref="Untranslatable"/> when it is not constant-like.</summary>
    public string TranslateConstant(string expression) => Ex(SyntaxFactory.ParseExpression(expression), top: true);

    private void CollectLocals(SyntaxNode root)
    {
        foreach (var v in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (v.Initializer?.Value is ObjectCreationExpressionSyntax oc && _scope.Units.ContainsKey(SimpleTypeName(oc.Type)))
                _formLocals[v.Identifier.Text] = SimpleTypeName(oc.Type);
            else
            {
                _locals.Add(v.Identifier.Text);
                if (v.Parent is VariableDeclarationSyntax vd && IsOpaqueType(vd.Type)) _opaqueLocals.Add(v.Identifier.Text);
            }
        }
        foreach (var a in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            if (a.Left is IdentifierNameSyntax target) _reassigned.Add(target.Identifier.Text);
        foreach (var u in root.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>().Where(u => u.IsKind(SyntaxKind.PreIncrementExpression) || u.IsKind(SyntaxKind.PreDecrementExpression)))
            if (u.Operand is IdentifierNameSyntax t1) _reassigned.Add(t1.Identifier.Text);
        foreach (var u in root.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
            if (u.Operand is IdentifierNameSyntax t2) _reassigned.Add(t2.Identifier.Text);
        foreach (var arg in root.DescendantNodes().OfType<ArgumentSyntax>().Where(x => !x.RefKindKeyword.IsKind(SyntaxKind.None)))
            if (arg.Expression is IdentifierNameSyntax t3) _reassigned.Add(t3.Identifier.Text);

        foreach (var f in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            _locals.Add(f.Identifier.Text);
            if (IsOpaqueType(f.Type)) _opaqueLocals.Add(f.Identifier.Text);
        }
        foreach (var c in root.DescendantNodes().OfType<CatchDeclarationSyntax>())
            if (c.Identifier.Text is { Length: > 0 } name) { _locals.Add(name); _catchVars.Add(name); }
    }

    private bool IsOpaqueType(TypeSyntax type) =>
        type.ToString() != "var" && _scope.Vocabulary.MapType(type.ToString(), new HashSet<string>()) == "unknown";

    /// <summary>The name when the expression is a local/field whose declared type cannot exist in the browser.</summary>
    private string? OpaqueName(ExpressionSyntax e)
    {
        var name = e switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } m => m.Name.Identifier.Text,
            _ => null
        };
        if (name is null) return null;
        if (_opaqueLocals.Contains(name)) return name;
        return !_locals.Contains(name) && _scope.Fields.TryGetValue(name, out var field)
               && _scope.Vocabulary.MapType(field.Type, new HashSet<string>()) == "unknown" ? name : null;
    }

    // ------------------------------------------------------------------------------------------------- statements

    private List<string> Stmt(StatementSyntax s)
    {
        _after.Clear();
        try
        {
            var lines = StmtCore(s);
            lines.AddRange(_after);
            _after.Clear();
            return lines;
        }
        catch (Dropped d) when (s is ExpressionStatementSyntax)
        {
            _after.Clear();
            return new List<string> { $"// dropped (no browser equivalent: {d.Message}): {Collapse(s)}" };
        }
        catch (Untranslatable u)
        {
            _after.Clear();
            Poison(s);
            return Fallback(s, u);
        }
    }

    private void Poison(StatementSyntax s)
    {
        var declaration = s switch
        {
            LocalDeclarationStatementSyntax ld => ld.Declaration,
            UsingStatementSyntax { Declaration: { } d } => d,
            _ => null
        };
        if (declaration is null) return;
        foreach (var v in declaration.Variables) _poisoned.Add(v.Identifier.Text);
    }

    private List<string> Fallback(StatementSyntax s, Untranslatable u)
    {
        Todos++;
        Reasons.Add(u.Message);
        var lines = new List<string>
        {
            u.WireToRealApi ? $"// TODO: wire to real API — {u.Message}" : $"// TODO: not translated — {u.Message}"
        };
        foreach (var l in s.ToFullString().Trim().Split('\n')) lines.Add("// " + l.TrimEnd('\r').TrimEnd());
        return lines;
    }

    private static string Collapse(SyntaxNode n) => Regex.Replace(n.ToString(), @"\s+", " ").Trim();

    private static List<string> Ind(IEnumerable<string> lines) => lines.Select(l => l.Length == 0 ? l : "  " + l).ToList();

    private List<string> Body(StatementSyntax s) => s is BlockSyntax b ? b.Statements.SelectMany(Stmt).ToList() : Stmt(s);

    private List<string> StmtCore(StatementSyntax s)
    {
        switch (s)
        {
            case BlockSyntax b:
                return b.Statements.SelectMany(Stmt).ToList();

            case EmptyStatementSyntax:
                return new List<string>();

            case ExpressionStatementSyntax es:
                return ExpressionStatement(es.Expression);

            case LocalDeclarationStatementSyntax ld:
                return LocalDeclaration(ld.Declaration, ld.IsConst);

            case ReturnStatementSyntax r:
                return new List<string> { r.Expression is null ? "return;" : $"return {Ex(r.Expression, top: true)};" };

            case BreakStatementSyntax: return new List<string> { "break;" };
            case ContinueStatementSyntax: return new List<string> { "continue;" };

            case ThrowStatementSyntax t:
                if (t.Expression is null) return new List<string> { _catchVars.Count > 0 ? $"throw {_catchVars.First()};" : "throw new Error();" };
                return new List<string> { $"throw {Ex(t.Expression)};" };

            case IfStatementSyntax i:
                return If(i);

            case WhileStatementSyntax w:
                return Wrap($"while ({Ex(w.Condition)}) {{", Body(w.Statement), "}");

            case DoStatementSyntax d:
                return Wrap("do {", Body(d.Statement), $"}} while ({Ex(d.Condition)});");

            case ForStatementSyntax f:
                return For(f);

            case ForEachStatementSyntax fe:
                return Wrap($"for (const {fe.Identifier.Text} of {Ex(fe.Expression)}) {{", Body(fe.Statement), "}");

            case SwitchStatementSyntax sw:
                return Switch(sw);

            case TryStatementSyntax tr:
                return Try(tr);

            case UsingStatementSyntax u:
                return Using(u);

            case LockStatementSyntax: throw new Untranslatable("lock statement (browser code is single-threaded; restructure manually)");
            case LocalFunctionStatementSyntax: throw new Untranslatable("local function");
            case GotoStatementSyntax or LabeledStatementSyntax: throw new Untranslatable("goto / labeled statement");
            case YieldStatementSyntax: throw new Untranslatable("iterator (yield)");
            default: throw new Untranslatable($"statement kind {s.Kind()}");
        }
    }

    private static List<string> Wrap(string open, List<string> body, string close)
    {
        var lines = new List<string> { open };
        if (body.Count == 0) body = new List<string> { "// (empty in the original)" };
        lines.AddRange(Ind(body));
        lines.Add(close);
        return lines;
    }

    private List<string> If(IfStatementSyntax i)
    {
        var lines = Wrap($"if ({Ex(i.Condition)}) {{", Body(i.Statement), "}");
        for (var e = i.Else; e is not null;)
        {
            if (e.Statement is IfStatementSyntax chained)
            {
                lines[^1] = $"}} else if ({Ex(chained.Condition)}) {{";
                lines.AddRange(Ind(Body(chained.Statement)));
                lines.Add("}");
                e = chained.Else;
            }
            else
            {
                lines[^1] = "} else {";
                lines.AddRange(Ind(Body(e.Statement)));
                lines.Add("}");
                e = null;
            }
        }
        return lines;
    }

    private List<string> For(ForStatementSyntax f)
    {
        string init;
        if (f.Declaration is { } decl)
        {
            var vars = decl.Variables.Select(v => $"{v.Identifier.Text}{(v.Initializer is null ? "" : " = " + Ex(v.Initializer.Value))}");
            init = "let " + string.Join(", ", vars);
        }
        else init = string.Join(", ", f.Initializers.Select(x => Ex(x)));
        var cond = f.Condition is null ? "" : Ex(f.Condition);
        var inc = string.Join(", ", f.Incrementors.Select(x => Ex(x)));
        return Wrap($"for ({init}; {cond}; {inc}) {{", Body(f.Statement), "}");
    }

    private List<string> Switch(SwitchStatementSyntax sw)
    {
        var lines = new List<string> { $"switch ({Ex(sw.Expression)}) {{" };
        foreach (var section in sw.Sections)
        {
            foreach (var label in section.Labels)
            {
                switch (label)
                {
                    case CaseSwitchLabelSyntax c: lines.Add($"  case {Ex(c.Value)}:"); break;
                    case DefaultSwitchLabelSyntax: lines.Add("  default:"); break;
                    default: throw new Untranslatable("pattern-based switch label");
                }
            }
            lines.AddRange(Ind(Ind(section.Statements.SelectMany(Stmt))));
        }
        lines.Add("}");
        return lines;
    }

    private List<string> Try(TryStatementSyntax tr)
    {
        var lines = Wrap("try {", Body(tr.Block), "}");
        if (tr.Catches.Count > 0)
        {
            var first = tr.Catches[0];
            if (first.Filter is not null) throw new Untranslatable("exception filter (catch ... when)");
            var name = first.Declaration?.Identifier.Text is { Length: > 0 } n ? n : "_ex";
            lines[^1] = $"}} catch ({name}) {{";
            if (tr.Catches.Count > 1)
            {
                Todos++;
                Reasons.Add("multiple catch clauses");
                lines.Add($"  // TODO: not translated — {tr.Catches.Count - 1} more catch clause(s) ({string.Join(", ", tr.Catches.Skip(1).Select(c => c.Declaration?.Type.ToString() ?? "any"))}); only the first is kept");
            }
            var catchBody = Body(first.Block);
            lines.AddRange(Ind(catchBody.Count == 0 ? new List<string> { "// (empty in the original)" } : catchBody));
            lines.Add("}");
        }
        if (tr.Finally is { } fin)
        {
            lines[^1] = "} finally {";
            var finallyBody = Body(fin.Block);
            lines.AddRange(Ind(finallyBody.Count == 0 ? new List<string> { "// (empty in the original)" } : finallyBody));
            lines.Add("}");
        }
        return lines;
    }

    private List<string> Using(UsingStatementSyntax u)
    {
        var lines = new List<string>();
        if (u.Declaration is { } decl)
        {
            lines.AddRange(LocalDeclaration(decl, isConst: false));
        }
        else if (u.Expression is not null && u.Expression is not ObjectCreationExpressionSyntax)
        {
            lines.Add(Ex(u.Expression, top: true) + ";");
        }
        lines.AddRange(Body(u.Statement)); // disposal has no meaning in the browser
        return lines;
    }

    private List<string> ExpressionStatement(ExpressionSyntax e)
    {
        var text = Ex(e, top: true);
        if (text.Length == 0) return new List<string>();
        return new List<string> { text + ";" };
    }

    private List<string> LocalDeclaration(VariableDeclarationSyntax decl, bool isConst)
    {
        var lines = new List<string>();
        foreach (var v in decl.Variables)
        {
            var name = v.Identifier.Text;
            if (_formLocals.ContainsKey(name))
            {
                lines.Add($"// {name}: a {_formLocals[name]} dialog; it is opened where ShowDialog() is called");
                if (v.Initializer?.Value is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 } creation && _scope.Generated.Contains(_formLocals[name]))
                    lines.Add($"// TODO: constructor arguments ({Collapse(creation.ArgumentList!)}) are not passed to {_scope.Components[_formLocals[name]]}");
                continue;
            }

            var init = v.Initializer?.Value;
            var keyword = isConst || (init is not null && !_reassigned.Contains(name)) ? "const" : "let";

            // DialogResult r = MessageBox.Show(..., buttons) / frm.ShowDialog()
            if (init is not null && decl.Type.ToString() is "DialogResult" or "var" && IsMessageBoxShow(init, out var mb) && HasButtons(mb))
            {
                _confirmLocals.Add(name);
                lines.Add($"const {name} = {Ex(init, top: true)};");
                continue;
            }

            var annotation = Annotation(decl.Type, init);
            if (init is null) lines.Add($"let {name}{annotation};");
            else lines.Add($"{keyword} {name}{annotation} = {Ex(init, top: true)};");
        }
        return lines;
    }

    /// <summary>Type annotation only where inference would be wrong or impossible: empty collections, null initializers, no initializer.</summary>
    private string Annotation(TypeSyntax declared, ExpressionSyntax? init)
    {
        var typeText = declared.ToString();
        if (typeText == "var" && init is ObjectCreationExpressionSyntax oc) typeText = oc.Type.ToString();
        if (typeText == "var") return "";

        var ts = _scope.Vocabulary.MapType(typeText, _scope.Output.TypeImports);
        if (ts.Contains("unknown")) return "";

        var isCollectionCreation = init is ObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax;
        if (init is null || isCollectionCreation) return ": " + ts;
        if (init is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression }) return ": " + (ts.EndsWith("| null") ? ts : ts + " | null");
        return "";
    }

    // ------------------------------------------------------------------------------------------------ expressions

    private string Ex(ExpressionSyntax e, bool top = false)
    {
        switch (e)
        {
            case LiteralExpressionSyntax lit: return Literal(lit);
            case IdentifierNameSyntax id: return Identifier(id.Identifier.Text);
            case ParenthesizedExpressionSyntax p: return "(" + Ex(p.Expression) + ")";
            case MemberAccessExpressionSyntax ma: return MemberAccess(ma);
            case InvocationExpressionSyntax inv: return Invoke(inv, top);
            case AssignmentExpressionSyntax a: return Assign(a);
            case BinaryExpressionSyntax b: return Binary(b);
            case PrefixUnaryExpressionSyntax pu: return pu.OperatorToken.Text + Ex(pu.Operand);
            case PostfixUnaryExpressionSyntax po: return Ex(po.Operand) + po.OperatorToken.Text;
            case ConditionalExpressionSyntax c: return $"{Ex(c.Condition)} ? {Ex(c.WhenTrue)} : {Ex(c.WhenFalse)}";
            case ElementAccessExpressionSyntax el: return $"{Ex(el.Expression)}[{string.Join(", ", el.ArgumentList.Arguments.Select(a2 => Ex(a2.Expression)))}]";
            case CastExpressionSyntax cast: return Cast(cast);
            case InterpolatedStringExpressionSyntax interp: return Interpolated(interp);
            case ObjectCreationExpressionSyntax oc: return Create(oc);
            case ArrayCreationExpressionSyntax ac: return ArrayCreate(ac);
            case ImplicitArrayCreationExpressionSyntax ia: return "[" + string.Join(", ", ia.Initializer.Expressions.Select(x => Ex(x))) + "]";
            case InitializerExpressionSyntax { RawKind: (int)SyntaxKind.ArrayInitializerExpression } init: return "[" + string.Join(", ", init.Expressions.Select(x => Ex(x))) + "]";
            case AwaitExpressionSyntax aw: return "await " + Ex(aw.Expression);
            case ConditionalAccessExpressionSyntax ca: return ConditionalAccess(ca);
            case IsPatternExpressionSyntax ip: return IsPattern(ip);
            case ThisExpressionSyntax: throw new Untranslatable("`this` used as a value");
            case DefaultExpressionSyntax or TypeOfExpressionSyntax: throw new Untranslatable("typeof/default expression");
            case SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax or AnonymousMethodExpressionSyntax: throw new Untranslatable("lambda / anonymous method");
            case QueryExpressionSyntax: throw new Untranslatable("LINQ query expression");
            default: throw new Untranslatable($"expression kind {e.Kind()}");
        }
    }

    private static string Str(string value) => JsonSerializer.Serialize(value, JsonOut);

    private static string Literal(LiteralExpressionSyntax lit)
    {
        switch (lit.Kind())
        {
            case SyntaxKind.StringLiteralExpression: return Str((string)lit.Token.Value!);
            case SyntaxKind.CharacterLiteralExpression: return Str(((char)lit.Token.Value!).ToString());
            case SyntaxKind.TrueLiteralExpression: return "true";
            case SyntaxKind.FalseLiteralExpression: return "false";
            case SyntaxKind.NullLiteralExpression: return "null";
            case SyntaxKind.NumericLiteralExpression:
                return Regex.Replace(lit.Token.Text, @"[fFdDmMlLuU]+$", "").Replace("_", "");
            default: throw new Untranslatable($"literal {lit.Kind()}");
        }
    }

    private string Identifier(string name)
    {
        if (_poisoned.Contains(name)) throw new Untranslatable($"depends on '{name}', whose declaration was not translated");
        if (_locals.Contains(name)) return name;
        if (_formLocals.ContainsKey(name)) throw new Untranslatable($"dialog variable '{name}' used as a value (its properties/results are not translated)");
        if (_scope.Fields.ContainsKey(name))
        {
            if (_constantContext) throw new Untranslatable($"field '{name}' referenced in an initializer");
            _scope.Output.UsesFieldModel = true;
            return "f." + name;
        }
        if (name == _scope.RootId || _scope.Controls.Contains(name)) throw new Untranslatable($"control '{name}' used as a value");
        throw new Untranslatable($"{name} not resolvable from IR", wireToRealApi: true);
    }

    private string MemberAccess(MemberAccessExpressionSyntax ma)
    {
        var name = ma.Name.Identifier.Text;
        var left = ma.Expression;

        // this.X -> X
        if (left is ThisExpressionSyntax)
        {
            if (name is "DialogResult" or "Cursor") throw new Dropped(name);
            if (_scope.Controls.Contains(name)) throw new Untranslatable($"control '{name}' used as a value");
            if (!_scope.Fields.ContainsKey(name) && !_scope.Functions.ContainsKey(name))
            {
                // this.Text / this.Enabled ...: properties of the form itself live in the root control's model entry.
                if (ModelProps.Contains(name))
                {
                    _scope.Output.UsesViewModel = true;
                    return $"vm.{_scope.RootId}.{char.ToLowerInvariant(name[0])}{name[1..]}";
                }
                if (VisualProps.Contains(name)) throw new Dropped($"this.{name} is appearance-only");
            }
            return Identifier(name);
        }

        // control.Property
        var controlId = ControlOf(left);
        if (controlId is not null)
        {
            if (ModelProps.Contains(name))
            {
                _scope.Output.UsesViewModel = true;
                return $"vm.{controlId}.{char.ToLowerInvariant(name[0])}{name[1..]}";
            }
            if (VisualProps.Contains(name)) throw new Dropped($"{controlId}.{name} is appearance-only");
            throw new Untranslatable($"control member {controlId}.{name} (not supported by the library or the model yet)");
        }

        // Static members of well-known types.
        var typeName = StaticTypeName(left);
        if (typeName is not null)
        {
            switch (typeName, name)
            {
                case ("string" or "String", "Empty"): return "\"\"";
                case ("Environment", "NewLine"): return "\"\\n\"";
                case ("DateTime", "Now" or "UtcNow"): return "new Date().toISOString()";
                case ("DateTime", "Today"): return "new Date().toISOString().slice(0, 10)";
                case ("DialogResult", _): return Str(name);
                case ("Application", "ProductName"): return "document.title";
                case ("Application", "ProductVersion" or "StartupPath" or "ExecutablePath"):
                    throw new Untranslatable($"Application.{name} (no browser equivalent)");
                case ("Cursor", _) or ("Cursors", _): throw new Dropped("cursor");
            }
            throw new Untranslatable($"{typeName}.{name} not resolvable from IR", wireToRealApi: true);
        }

        // catch variable members.
        if (left is IdentifierNameSyntax cv && _catchVars.Contains(cv.Identifier.Text))
        {
            return name switch
            {
                "Message" => $"({cv.Identifier.Text} instanceof Error ? {cv.Identifier.Text}.message : String({cv.Identifier.Text}))",
                "StackTrace" => $"({cv.Identifier.Text} as Error).stack",
                _ => throw new Untranslatable($"exception member {name}")
            };
        }

        if (OpaqueName(left) is { } opaque) throw new Untranslatable($"member access on '{opaque}', whose type has no browser equivalent");

        var receiver = Ex(left);
        return name switch
        {
            "Length" or "Count" => $"{receiver}.length",
            _ => $"{receiver}.{name}"
        };
    }

    /// <summary>The control id when the expression is `ctl` or `this.ctl` and names a control of this page.</summary>
    private string? ControlOf(ExpressionSyntax e)
    {
        var name = e switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } m => m.Name.Identifier.Text,
            _ => null
        };
        if (name is null || _locals.Contains(name) || _scope.Fields.ContainsKey(name)) return null;
        return _scope.Controls.Contains(name) || name == _scope.RootId ? name : null;
    }

    /// <summary>Simple type name for `string`, `Math`, `ECMLibrary.UCM`, ... when the expression denotes a type, not a variable.</summary>
    private string? StaticTypeName(ExpressionSyntax e)
    {
        switch (e)
        {
            case PredefinedTypeSyntax p: return p.Keyword.Text;
            case IdentifierNameSyntax id:
                var n = id.Identifier.Text;
                if (_locals.Contains(n) || _scope.Fields.ContainsKey(n) || _scope.Controls.Contains(n) || _formLocals.ContainsKey(n)) return null;
                return char.IsUpper(n[0]) || n is "string" or "int" ? n : null;
            case MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax ns } m when m.Name.Identifier.Text.Length > 0 && char.IsUpper(m.Name.Identifier.Text[0]) && ns.Identifier.Text is "ECMLibrary" or "System" or "Windows":
                return m.Name.Identifier.Text;
            default: return null;
        }
    }

    private static string SimpleTypeName(TypeSyntax t) => t switch
    {
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        IdentifierNameSyntax i => i.Identifier.Text,
        _ => t.ToString()
    };

    private string Cast(CastExpressionSyntax cast)
    {
        var inner = Ex(cast.Expression);
        return cast.Type.ToString() switch
        {
            "int" or "long" or "short" or "byte" or "double" or "float" or "decimal" or "uint" or "ulong" => $"Number({inner})",
            "string" => $"String({inner})",
            "bool" => $"Boolean({inner})",
            _ => inner
        };
    }

    private string Interpolated(InterpolatedStringExpressionSyntax s)
    {
        var sb = new StringBuilder("`");
        foreach (var part in s.Contents)
        {
            switch (part)
            {
                case InterpolatedStringTextSyntax t: sb.Append(EscapeTemplate(t.TextToken.ValueText)); break;
                case InterpolationSyntax i:
                    if (i.AlignmentClause is not null || i.FormatClause is not null) throw new Untranslatable("interpolation with alignment/format specifier");
                    sb.Append("${").Append(Ex(i.Expression)).Append('}');
                    break;
            }
        }
        return sb.Append('`').ToString();
    }

    private static string EscapeTemplate(string text) => text.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${").Replace("\r", "\\r").Replace("\n", "\\n");

    private string IsPattern(IsPatternExpressionSyntax ip) => ip.Pattern switch
    {
        ConstantPatternSyntax { Expression: LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression } } => $"{Ex(ip.Expression)} === null",
        UnaryPatternSyntax { Pattern: ConstantPatternSyntax { Expression: LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression } } } => $"{Ex(ip.Expression)} !== null",
        _ => throw new Untranslatable("type/pattern test (is)")
    };

    private string ConditionalAccess(ConditionalAccessExpressionSyntax ca)
    {
        var recv = Ex(ca.Expression);
        return ca.WhenNotNull switch
        {
            MemberBindingExpressionSyntax mb => $"{recv}?.{mb.Name.Identifier.Text}",
            InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax mb2 } inv when InstanceSuffix(mb2.Name.Identifier.Text, inv.ArgumentList.Arguments.Select(a => Ex(a.Expression)).ToList()) is { } suffix
                => $"{recv}?{suffix}",
            _ => throw new Untranslatable("conditional access (?.)")
        };
    }

    // -------------------------------------------------------------------------------------------- binary / assignment

    private string Binary(BinaryExpressionSyntax b)
    {
        var op = b.OperatorToken.Text;
        if (b.IsKind(SyntaxKind.AsExpression)) return Ex(b.Left);
        if (b.IsKind(SyntaxKind.IsExpression)) throw new Untranslatable("type test (is)");

        if (op is "==" or "!=" && TryDialogCompare(b, out var dialog)) return dialog;

        var left = Ex(b.Left);
        var right = Ex(b.Right);
        op = op switch { "==" => "===", "!=" => "!==", _ => op };
        return $"{left} {op} {right}";
    }

    private bool TryDialogCompare(BinaryExpressionSyntax b, out string result)
    {
        result = "";
        var negate = b.OperatorToken.Text == "!=";
        var (value, constant) = IsDialogResultMember(b.Right, out var rightName) ? (b.Left, rightName)
            : IsDialogResultMember(b.Left, out var leftName) ? (b.Right, leftName) : (null, null);
        if (value is null || constant is null) return false;

        // MessageBox.Show(..., YesNo) == DialogResult.Yes  ->  (await dialog.confirm(...))
        var isConfirmValue = (IsMessageBoxShow(value, out var mb) && HasButtons(mb))
                             || (value is IdentifierNameSyntax id && _confirmLocals.Contains(id.Identifier.Text));
        if (isConfirmValue)
        {
            var positive = constant is "Yes" or "OK";
            var text = value is IdentifierNameSyntax idv && _confirmLocals.Contains(idv.Identifier.Text) ? idv.Identifier.Text : Ex(value);
            var asBool = positive != negate ? text : $"!{text}";
            result = asBool;
            return true;
        }
        result = $"{Ex(value)} {(negate ? "!==" : "===")} {Str(constant)}";
        return true;
    }

    private static bool IsDialogResultMember(ExpressionSyntax e, out string name)
    {
        name = "";
        if (e is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "DialogResult" } } m)
        {
            name = m.Name.Identifier.Text;
            return true;
        }
        return false;
    }

    private string Assign(AssignmentExpressionSyntax a)
    {
        var op = a.OperatorToken.Text;

        // Event subscription in code: x.Click += handler
        if (a.IsKind(SyntaxKind.AddAssignmentExpression) && a.Left is MemberAccessExpressionSyntax { Name.Identifier.Text: var evt }
            && evt is "Click" or "TextChanged" or "SelectedIndexChanged" or "CheckedChanged" or "DoubleClick" or "KeyDown" or "KeyPress" or "Tick")
            throw new Untranslatable($"event subscription in code ({evt} += ...)");

        // this.DialogResult = DialogResult.OK / DialogResult = ...: remembered for the next Close()
        if (a.Left is IdentifierNameSyntax { Identifier.Text: "DialogResult" } || a.Left is MemberAccessExpressionSyntax { Name.Identifier.Text: "DialogResult", Expression: ThisExpressionSyntax })
        {
            _pendingResult = a.Right is MemberAccessExpressionSyntax rm ? rm.Name.Identifier.Text : null;
            throw new Dropped("DialogResult is passed to onClose(result)");
        }
        if (a.Left is MemberAccessExpressionSyntax { Name.Identifier.Text: "Current", Expression: IdentifierNameSyntax { Identifier.Text: "Cursor" } })
            throw new Dropped("cursor");
        if (a.Left is IdentifierNameSyntax { Identifier.Text: "Cursor" } || a.Left is MemberAccessExpressionSyntax { Name.Identifier.Text: "Cursor", Expression: ThisExpressionSyntax })
            throw new Dropped("cursor");

        return $"{Ex(a.Left)} {op} {NullBang(a.Right, Ex(a.Right, top: true))}";
    }

    /// <summary>A literal null passed or assigned to a typed target: `null!` has type never, so it type-checks like C# does.</summary>
    private static string NullBang(ExpressionSyntax source, string text) =>
        source is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression) ? "null!" : text;

    // ------------------------------------------------------------------------------------------------ object creation

    private string Create(ObjectCreationExpressionSyntax oc)
    {
        var type = SimpleTypeName(oc.Type);
        var args = oc.ArgumentList?.Arguments.ToList() ?? new List<ArgumentSyntax>();

        if (type is "List" or "ArrayList" or "HashSet" or "Queue" or "Stack")
        {
            if (oc.Initializer is { } init) return "[" + string.Join(", ", init.Expressions.Select(x => Ex(x))) + "]";
            return args.Count == 1 ? $"[...{Ex(args[0].Expression)}]" : "[]";
        }
        if (type is "Dictionary" or "SortedDictionary" or "Hashtable" && args.Count == 0 && oc.Initializer is null) return "{}";
        if (type.EndsWith("Exception", StringComparison.Ordinal) || type == "Exception")
            return $"new Error({string.Join(", ", args.Take(1).Select(x => Ex(x.Expression)))})";
        if (type == "Guid" && args.Count == 0) return "crypto.randomUUID()";
        if (args.Count == 0 && _scope.Vocabulary.DtoTypeName(type) is { } dto)
        {
            // A contract data type: an object literal asserted to the generated interface (properties not listed stay undefined).
            _scope.Output.TypeImports.Add(dto);
            var fields = (oc.Initializer?.Expressions ?? default).Select(x => x is AssignmentExpressionSyntax { Left: IdentifierNameSyntax n } a
                ? $"{n.Identifier.Text}: {Ex(a.Right)}" : throw new Untranslatable("collection initializer on a data type"));
            var list = string.Join(", ", fields);
            return list.Length == 0 ? $"{{}} as {dto}" : $"{{ {list} }} as {dto}";
        }
        throw new Untranslatable($"construction of {oc.Type} not resolvable from IR", wireToRealApi: true);
    }

    private string ArrayCreate(ArrayCreationExpressionSyntax ac)
    {
        if (ac.Initializer is { } init) return "[" + string.Join(", ", init.Expressions.Select(x => Ex(x))) + "]";
        if (ac.Type.RankSpecifiers.Count == 1 && ac.Type.RankSpecifiers[0].Sizes.Count == 1)
            return $"new Array({Ex(ac.Type.RankSpecifiers[0].Sizes[0])})";
        throw new Untranslatable("array creation");
    }

    // ---------------------------------------------------------------------------------------------------- invocation

    private static bool IsMessageBoxShow(ExpressionSyntax e, out InvocationExpressionSyntax inv)
    {
        inv = null!;
        if (e is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Show", Expression: var recv } } i
            && recv.ToString() is "MessageBox" or "System.Windows.Forms.MessageBox")
        {
            inv = i;
            return true;
        }
        return false;
    }

    private static bool HasButtons(InvocationExpressionSyntax mb)
    {
        var args = mb.ArgumentList.Arguments;
        return args.Count >= 3 && args[2].Expression.ToString() is var b && b.Contains("YesNo") | b.Contains("OKCancel") | b.Contains("RetryCancel");
    }

    private static string Await(string call, bool top) => top ? $"await {call}" : $"(await {call})";

    private List<string> Args(InvocationExpressionSyntax inv)
    {
        var list = new List<string>();
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (!arg.RefKindKeyword.IsKind(SyntaxKind.None)) throw new Untranslatable("ref/out argument");
            if (arg.NameColon is not null) throw new Untranslatable("named argument");
            list.Add(NullBang(arg.Expression, Ex(arg.Expression)));
        }
        return list;
    }

    private string Invoke(InvocationExpressionSyntax inv, bool top)
    {
        if (_constantContext) throw new Untranslatable("method call in an initializer or default value");

        // Bare call: own method.
        if (inv.Expression is IdentifierNameSyntax bare)
        {
            if (bare.Identifier.Text is "Close" or "Hide" && !_scope.Functions.ContainsKey(bare.Identifier.Text)) return CloseCall();
            return CallOwn(bare.Identifier.Text, inv, top);
        }

        if (inv.Expression is not MemberAccessExpressionSyntax ma) throw new Untranslatable("call through an expression");
        var method = ma.Name.Identifier.Text;
        var receiver = ma.Expression;

        if (receiver is ThisExpressionSyntax)
        {
            if (method is "Close" or "Hide" && !_scope.Functions.ContainsKey(method)) return CloseCall();
            if (method is "Show" or "ShowDialog" or "Activate" or "Focus") throw new Untranslatable($"this.{method}()");
            return CallOwn(method, inv, top);
        }

        // MessageBox.Show(...)
        if (IsMessageBoxShow(inv, out _)) return MessageBox(inv, top);

        // Forms: new frmX(...).ShowDialog() / f.ShowDialog()
        if (method is "ShowDialog" or "Show")
        {
            var formType = receiver switch
            {
                ObjectCreationExpressionSyntax oc when _scope.Units.ContainsKey(SimpleTypeName(oc.Type)) => SimpleTypeName(oc.Type),
                IdentifierNameSyntax id when _formLocals.TryGetValue(id.Identifier.Text, out var t) => t,
                _ => null
            };
            if (formType is not null) return ShowForm(formType, receiver is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 }, top);
        }

        var typeName = StaticTypeName(receiver);
        if (typeName is not null) return StaticCall(typeName, method, inv, top);

        // control.Method(...) is not translated (focus, Items.Add, Select, ...)
        if (ControlOf(receiver) is { } cid) throw new Untranslatable($"call to control member {cid}.{method}(...) (not supported by the library or the model yet)");
        if (receiver is MemberAccessExpressionSyntax { Expression: var inner } && ControlOf(inner) is { } cid2)
            throw new Untranslatable($"call to {cid2}.{((MemberAccessExpressionSyntax)receiver).Name.Identifier.Text}.{method}(...) (collection UI such as ListView/grid is not translated yet)");

        // main.WriteErrorLog(MethodBase.GetCurrentMethod(), ex): a call on an app-level helper we cannot see; error logging maps to the console.
        if (IsLoggingMethod(method) && receiver is IdentifierNameSyntax unresolved && !IsKnownName(unresolved.Identifier.Text))
            return $"console.error({string.Join(", ", Args(inv))})";

        if (OpaqueName(receiver) is { } opaqueRecv) throw new Untranslatable($"call on '{opaqueRecv}', whose type has no browser equivalent");

        // Instance method on a value.
        var args = Args(inv);
        var recv = Ex(receiver);
        if (method == "ToString" && args.Count == 0) return $"String({recv})";
        if (method == "ContainsKey" && args.Count == 1) return $"({args[0]} in {recv})";
        if (InstanceSuffix(method, args) is { } suffix) return recv + suffix;
        throw new Untranslatable($"call to {Collapse(receiver)}.{method}(...) not resolvable from IR", wireToRealApi: true);
    }

    private static bool IsLoggingMethod(string method) => method is "WriteErrorLog" or "LogError" or "WriteLog" or "LogException" or "WriteError" or "LogMessage";

    private bool IsKnownName(string name) =>
        _locals.Contains(name) || _poisoned.Contains(name) || _scope.Fields.ContainsKey(name) || _scope.Controls.Contains(name) || _formLocals.ContainsKey(name);

    private string CloseCall()
    {
        _scope.Output.UsesOnClose = true;
        var result = _pendingResult is null ? "" : Str(_pendingResult);
        _pendingResult = null;
        return $"onClose?.({result})";
    }

    private string CallOwn(string name, InvocationExpressionSyntax inv, bool top)
    {
        var argc = inv.ArgumentList.Arguments.Count;
        var fn = _scope.Resolve(name, argc) ?? throw new Untranslatable($"call to {name}(...) not resolvable from IR", wireToRealApi: true);
        var args = fn.IgnoresArgs ? new List<string>() : Args(inv);
        var call = $"{fn.TsName}({string.Join(", ", args)})";
        return fn.IsAsync ? Await(call, top) : call;
    }

    private string MessageBox(InvocationExpressionSyntax inv, bool top)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count == 0) throw new Untranslatable("MessageBox.Show()");
        _scope.Output.UsesDialog = true;

        var text = Ex(args[0].Expression);
        var caption = args.Count >= 2 ? ", " + Ex(args[1].Expression) : "";
        var confirm = HasButtons(inv);
        var call = $"dialog.{(confirm ? "confirm" : "alert")}({text}{caption})";
        return Await(call, top);
    }

    private string ShowForm(string formType, bool hasArgs, bool top)
    {
        _scope.Output.UsesDialog = true;
        if (!_scope.Generated.Contains(formType))
        {
            _after.Add($"// TODO: form {formType} is outside the generated scope (see MIGRATION_REPORT.md)");
            return Await($"dialog.alert({Str($"Form {formType} is not part of this generated app.")})", top);
        }

        var component = _scope.Components[formType];
        _scope.Output.PageImports.Add(component);
        if (hasArgs) _after.Add($"// TODO: constructor arguments of {formType} are not passed to {component}");
        var title = Str(_scope.Units[formType].Title);
        return Await($"dialog.show<unknown>((close) => <{component} onClose={{close}} />, {{ title: {title} }})", top);
    }

    private string StaticCall(string type, string method, InvocationExpressionSyntax inv, bool top)
    {
        // External contract facade -> generated service
        if (_scope.Vocabulary.IsFacade(type))
        {
            var sig = _scope.Vocabulary.FindCall(type, method, inv.ArgumentList.Arguments.Count)
                      ?? throw new Untranslatable($"call to {type}.{method}(...) not resolvable from IR", wireToRealApi: true);
            var all = Args(inv);
            var kept = string.Join(", ", sig.Keep.Select(i => all[i]));
            _scope.Output.UsesService = true;
            return Await($"{_scope.Vocabulary.InstanceName}.{type}.{method}({kept})", top);
        }

        if (IsLoggingMethod(method)) return $"console.error({string.Join(", ", Args(inv))})";

        switch (type, method)
        {
            case ("Application", "DoEvents"): throw new Dropped("Application.DoEvents");
            case ("MethodBase", "GetCurrentMethod"): return Str(_method?.Name ?? ""); // used only to label log entries
            case ("Application", "Exit"): throw new Untranslatable("Application.Exit() has no browser equivalent");
            case ("Cursor", _): throw new Dropped("cursor");
        }

        var args = Args(inv);
        switch (type, method)
        {
            case ("string" or "String", "IsNullOrEmpty") when args.Count == 1: return $"!{Operand(args[0])}";
            case ("string" or "String", "IsNullOrWhiteSpace") when args.Count == 1: return $"!{Operand(args[0])}?.trim()";
            case ("string" or "String", "Join") when args.Count == 2: return $"{Operand(args[1])}.join({args[0]})";
            case ("string" or "String", "Format"): return Format(inv);
            case ("Convert", "ToInt32" or "ToInt64" or "ToInt16" or "ToByte") or ("int" or "Int32" or "long" or "Int64", "Parse") when args.Count == 1:
                return $"Number.parseInt(String({args[0]}), 10)";
            case ("Convert", "ToDouble" or "ToDecimal" or "ToSingle") or ("double" or "Double" or "decimal" or "Decimal" or "float", "Parse") when args.Count == 1:
                return $"Number.parseFloat(String({args[0]}))";
            case ("Convert", "ToString") when args.Count == 1: return $"String({args[0]})";
            case ("Convert", "ToBoolean") when args.Count == 1: return $"Boolean({args[0]})";
            case ("Math", "Max" or "Min" or "Abs" or "Round" or "Floor" or "Pow" or "Sqrt"): return $"Math.{char.ToLowerInvariant(method[0])}{method[1..]}({string.Join(", ", args)})";
            case ("Math", "Ceiling"): return $"Math.ceil({string.Join(", ", args)})";
            case ("DateTime", "Parse") when args.Count == 1: return $"new Date({args[0]}).toISOString()";
            case ("Guid", "NewGuid"): return "crypto.randomUUID()";
            case ("Path" or "File" or "Directory" or "Process" or "Registry", _):
                throw new Untranslatable($"{type}.{method}: file system / process access has no browser equivalent (needs a server-side service)");
        }
        throw new Untranslatable($"call to {type}.{method}(...) not resolvable from IR", wireToRealApi: true);
    }

    /// <summary>Wraps an operand for a prefix operator when it is not a simple access chain.</summary>
    private static string Operand(string text) => Regex.IsMatch(text, @"^[\w.\[\]""?]+$") ? text : $"({text})";

    private string Format(InvocationExpressionSyntax inv)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count == 0) throw new Untranslatable("string.Format()");
        if (args[0].Expression is not LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } fmt)
        {
            // Format string held in a constant/field: resolved at run time by a tiny helper (format specifiers are ignored).
            _scope.Output.UsesFormat = true;
            return $"formatString({string.Join(", ", args.Select(a => Ex(a.Expression)))})";
        }

        var values = args.Skip(1).Select(a => Ex(a.Expression)).ToList();
        var text = (string)fmt.Token.Value!;
        var sb = new StringBuilder("`");
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{' && i + 1 < text.Length && text[i + 1] == '{') { sb.Append('{'); i++; continue; }
            if (text[i] == '}' && i + 1 < text.Length && text[i + 1] == '}') { sb.Append('}'); i++; continue; }
            if (text[i] == '{')
            {
                var end = text.IndexOf('}', i);
                var spec = end < 0 ? "" : text[(i + 1)..end];
                if (end < 0 || !int.TryParse(spec, out var index) || index >= values.Count) throw new Untranslatable("string.Format placeholder with a format specifier or missing argument");
                sb.Append("${").Append(values[index]).Append('}');
                i = end;
                continue;
            }
            sb.Append(EscapeTemplate(text[i].ToString()));
        }
        return sb.Append('`').ToString();
    }

    /// <summary>`.method(args)` for string/array instance methods with a direct JavaScript counterpart; null when there is none.</summary>
    private static string? InstanceSuffix(string method, List<string> a) => (method, a.Count) switch
    {
        ("Trim", 0) => ".trim()",
        ("TrimStart", 0) => ".trimStart()",
        ("TrimEnd", 0) => ".trimEnd()",
        ("ToUpper" or "ToUpperInvariant", 0) => ".toUpperCase()",
        ("ToLower" or "ToLowerInvariant", 0) => ".toLowerCase()",
        ("Contains", 1) => $".includes({a[0]})",
        ("StartsWith", 1) => $".startsWith({a[0]})",
        ("EndsWith", 1) => $".endsWith({a[0]})",
        ("Substring", 1) => $".substring({a[0]})",
        ("Substring", 2) => $".substring({a[0]}, ({a[0]}) + ({a[1]}))",
        ("IndexOf", 1) => $".indexOf({a[0]})",
        ("LastIndexOf", 1) => $".lastIndexOf({a[0]})",
        ("Replace", 2) => $".replaceAll({a[0]}, {a[1]})",
        ("Split", 1) => $".split({a[0]})",
        ("PadLeft", 1) => $".padStart({a[0]})",
        ("PadLeft", 2) => $".padStart({a[0]}, {a[1]})",
        ("PadRight", 1) => $".padEnd({a[0]})",
        ("PadRight", 2) => $".padEnd({a[0]}, {a[1]})",
        ("Add", 1) => $".push({a[0]})",
        ("Insert", 2) => $".splice({a[0]}, 0, {a[1]})",
        ("RemoveAt", 1) => $".splice({a[0]}, 1)",
        ("Clear", 0) => ".length = 0",
        ("ToArray" or "ToList", 0) => ".slice()",
        _ => null
    };
}
