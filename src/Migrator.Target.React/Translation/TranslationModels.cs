using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Target.React.Contracts;
using Migrator.Target.React.Generation;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.React.Translation;

public enum TranslationStatus { Complete, Partial, NotTranslated }

/// <summary>A construct the rules do not translate. Caught per statement: that statement stays as a commented original + TODO.</summary>
public class Untranslatable : Exception
{
    public Untranslatable(string reason, bool wireToRealApi = false) : base(reason) => WireToRealApi = wireToRealApi;

    /// <summary>True for references that cannot be resolved from the IR (CLAUDE.md §6 marker: "wire to real API").</summary>
    public bool WireToRealApi { get; }
}

/// <summary>A statement that has no browser meaning (Application.DoEvents, Cursor, DialogResult assignment...): dropped with a comment, not a TODO.</summary>
public sealed class Dropped : Untranslatable
{
    public Dropped(string reason) : base(reason) { }
}

/// <summary>One page method as a TypeScript function (or a decision to keep it as commented C#).</summary>
public sealed class MethodTranslation
{
    public MemberIr Member { get; init; } = null!;
    public string TsName { get; init; } = "";
    public bool IsAsync { get; set; }
    public bool IsWiredHandler { get; init; }
    public TranslationStatus Status { get; set; } = TranslationStatus.NotTranslated;

    /// <summary>Full function declaration (comment header, signature, body) when Status != NotTranslated.</summary>
    public List<string> Lines { get; set; } = new();

    public List<string> Reasons { get; } = new();
    public int Todos { get; set; }

    /// <summary>Windows-specific APIs whose statements were bypassed (one entry per statement).</summary>
    public List<string> Bypasses { get; } = new();
}

/// <summary>Everything the page generator needs from translating one page.</summary>
public sealed class PageTranslation
{
    public List<MethodTranslation> Methods { get; } = new();
    public Dictionary<MemberIr, MethodTranslation> ByMember { get; } = new();

    /// <summary>Field/property name -> TypeScript initial value, for the `f` model.</summary>
    public SortedDictionary<string, string> FieldInitials { get; } = new(StringComparer.Ordinal);

    public bool UsesDialog { get; set; }
    public bool UsesOnClose { get; set; }
    public bool UsesService { get; set; }
    /// <summary>True once translated code references `f.&lt;field&gt;`; the field model is declared only then (an unused declaration fails lint).</summary>
    public bool UsesFieldModel { get; set; }
    public bool UsesViewModel { get; set; }

    /// <summary>True when translated code calls the generated `formatString` helper (string.Format with a non-literal format).</summary>
    public bool UsesFormat { get; set; }
    public SortedSet<string> PageImports { get; } = new(StringComparer.Ordinal);

    /// <summary>Constructor arguments of the original form, offered as optional props (with mock defaults where the type allows).</summary>
    public List<CtorProp> CtorProps { get; } = new();

    /// <summary>Constructor statements that are not a plain `field = argument;` copy (kept as comments).</summary>
    public List<string> CtorNotes { get; } = new();
    public string FixturesImport { get; set; } = "";
    public SortedSet<string> TypeImports { get; } = new(StringComparer.Ordinal);
}

/// <summary>One constructor argument: prop key (C# name), local name in the component, TS type, mock default (null = none).</summary>
public sealed record CtorProp(string Key, string Local, string Type, string? Default);

/// <summary>A callable page method with its generated TypeScript name.</summary>
public sealed class MemberFunction
{
    public MemberIr Member { get; init; } = null!;
    public string TsName { get; init; } = "";
    public bool IsAsync { get; set; }

    /// <summary>Qualifier for calls from inside the same support class (<c>main.</c> or <c>this.</c>); empty for page functions.</summary>
    public string Prefix { get; init; } = "";

    /// <summary>Event-handler shape (object sender, EventArgs e): callers' arguments are dropped.</summary>
    public bool IgnoresArgs { get; init; }
}

/// <summary>What a method translation may reference on the page.</summary>
public sealed class PageScope
{
    public string RootId { get; init; } = "";
    public HashSet<string> Controls { get; } = new(StringComparer.Ordinal);

    /// <summary>Controls of type ListView: their Items/SelectedItems/Columns live in the model (`vm.&lt;id&gt;.items`).</summary>
    public HashSet<string> ListViews { get; } = new(StringComparer.Ordinal);

    /// <summary>Controls of type ComboBox: `Items` / `SelectedIndex` / `SelectedItem` live in the page model (`vm.<id>.items`, `.selectedIndex`, `.selected`).</summary>
    public HashSet<string> Combos { get; } = new(StringComparer.Ordinal);

    /// <summary>Controls of type TabControl: `SelectedIndex` is `vm.<id>.selectedIndex`.</summary>
    public HashSet<string> TabControls { get; } = new(StringComparer.Ordinal);

    /// <summary>Controls of type CheckedListBox: `CheckedIndices` / `CheckedItems` / `GetItemChecked` / `SetItemChecked` use `vm.<id>.checkedIndices`.</summary>
    public HashSet<string> CheckedLists { get; } = new(StringComparer.Ordinal);

    /// <summary>Controls of type TreeView: `Nodes` / `SelectedNode` live in the page model (`vm.&lt;id&gt;.nodes`, `.selectedNode`).</summary>
    public HashSet<string> TreeViews { get; } = new(StringComparer.Ordinal);

    /// <summary>Controls of type MonthCalendar: `SelectionStart` is `vm.<id>.selectionStart` (ISO date text).</summary>
    public HashSet<string> Calendars { get; } = new(StringComparer.Ordinal);

    /// <summary>The project's own non-UI classes (`main`, `Preferences`, enums, data classes), emitted as <c>src/support</c>.</summary>
    public IReadOnlyDictionary<string, SupportClassInfo> Support { get; init; } = new Dictionary<string, SupportClassInfo>();

    /// <summary>True when translating a support class: fields are `Class.x` / `this.x`, and UI (dialogs, closing) is not available.</summary>
    public bool IsModule { get; init; }
    public string OwnerName { get; init; } = "";

    /// <summary>Windows-specific APIs to bypass (data file); null disables bypassing.</summary>
    public BypassRules? Bypass { get; init; }

    /// <summary>Markers of server-side code: such methods of project classes become typed stubs.</summary>
    public ServerBoundRules ServerBound { get; init; } = ServerBoundRules.Empty;

    /// <summary>Control id -> WinForms member -> member of the replacement control (a control rendered by a fallback component: `Value` -> `Text`).</summary>
    public Dictionary<string, Dictionary<string, string>> Aliases { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, MemberIr> Fields { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<MemberFunction>> Functions { get; } = new(StringComparer.Ordinal);
    public ContractVocabulary Vocabulary { get; init; } = ContractVocabulary.Empty;
    public IReadOnlyDictionary<string, string> Components { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, UnitIr> Units { get; init; } = new Dictionary<string, UnitIr>();
    public ISet<string> Generated { get; init; } = new HashSet<string>();
    public PageTranslation Output { get; init; } = new();

    public MemberFunction? Resolve(string name, int argCount)
    {
        if (!Functions.TryGetValue(name, out var overloads)) return null;
        return overloads.FirstOrDefault(o => o.Member.Parameters.Count == argCount)
               ?? overloads.FirstOrDefault(o => o.Member.Parameters.Count(p => p.DefaultValue == null) <= argCount && argCount <= o.Member.Parameters.Count)
               ?? (overloads.Count == 1 ? overloads[0] : null);
    }
}

/// <summary>Facade calls and data types available from an external contract (see ServiceEmitter); empty when no contract is used.</summary>
public sealed class ContractVocabulary
{
    public static ContractVocabulary Empty { get; } = new(null);

    private readonly ContractEmitResult? _result;

    public ContractVocabulary(ContractEmitResult? result) => _result = result;

    /// <summary>Simple names of support classes/enums: valid TypeScript type names in generated code.</summary>
    public HashSet<string> SupportTypes { get; } = new(StringComparer.Ordinal);

    public string InstanceName => _result?.InstanceName ?? "";
    public string ServiceImport => _result?.ServiceImport ?? "";
    public string TypesImport => _result?.TypesImport ?? "";
    public string FixturesImport => TypesImport.EndsWith("/types", StringComparison.Ordinal) ? TypesImport[..^"/types".Length] + "/fixtures" : "";

    /// <summary>TypeScript name of an emitted contract data type or enum, by simple C# name; null when it was not emitted.</summary>
    public string? DtoTypeName(string simpleName) => _result is not null && _result.TypeNameBySimple.TryGetValue(simpleName, out var ts) ? ts : null;

    /// <summary>`System`/`Windows`, or a root namespace of the loaded contract: a name that qualifies a type rather than being a variable.</summary>
    public bool IsNamespaceRoot(string name) => name is "System" or "Windows" || (_result?.NamespaceRoots.Contains(name) ?? false);

    public bool IsFacade(string simpleName) => _result?.Facades.ContainsKey(simpleName) ?? false;

    /// <summary>Finds the overload with this many original arguments; Keep says which of them reach the generated service.</summary>
    public CallSignature? FindCall(string facade, string method, int argCount) =>
        _result is not null && _result.Facades.TryGetValue(facade, out var methods) && methods.TryGetValue(method, out var overloads)
            ? overloads.FirstOrDefault(o => o.ParamCount == argCount)
            : null;

    /// <summary>C# source type text -> TypeScript type text ("unknown" when it cannot be expressed). Emitted contract types are recorded in <paramref name="used"/>.</summary>
    public string MapType(string sourceType, ISet<string> used)
    {
        TypeSyntax type;
        try { type = SyntaxFactory.ParseTypeName(sourceType); }
        catch (Exception) { return "unknown"; }
        return Map(type, used);
    }

    private string Map(TypeSyntax type, ISet<string> used)
    {
        switch (type)
        {
            case NullableTypeSyntax n: return Map(n.ElementType, used) + " | null";
            case ArrayTypeSyntax { RankSpecifiers.Count: 1 } a when a.RankSpecifiers[0].Rank == 1:
                var element = Map(a.ElementType, used);
                return (element.Contains(' ') ? $"({element})" : element) + "[]";
            case PredefinedTypeSyntax p:
                return p.Keyword.Text switch
                {
                    "string" or "char" => "string",
                    "bool" => "boolean",
                    "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double" or "decimal" => "number",
                    _ => "unknown"
                };
            case GenericNameSyntax g:
                var args = g.TypeArgumentList.Arguments.Select(a2 => Map(a2, used)).ToList();
                if (g.Identifier.Text is "List" or "IList" or "IEnumerable" or "ICollection" or "HashSet" && args.Count == 1)
                    return (args[0].Contains(' ') ? $"({args[0]})" : args[0]) + "[]";
                if (g.Identifier.Text is "Dictionary" or "IDictionary" && args.Count == 2) return $"Record<{(args[0] == "number" ? "number" : "string")}, {args[1]}>";
                return "unknown";
            case QualifiedNameSyntax q: return Map(q.Right, used);
            case IdentifierNameSyntax id: return Simple(id.Identifier.Text, used);
            default: return "unknown";
        }
    }

    private string Simple(string name, ISet<string> used)
    {
        switch (name)
        {
            case "String": return "string";
            case "Int32" or "Int64" or "Double" or "Decimal": return "number";
            case "Boolean": return "boolean";
            case "Guid": return "string";
            case "DateTime": return "string";
            case "ListViewItem": return "WfListItem";
            case "TreeNode": return "WfTreeNode";
        }
        if (SupportTypes.Contains(name)) return name;
        if (_result is not null && _result.TypeNameBySimple.TryGetValue(name, out var ts))
        {
            used.Add(ts);
            return ts;
        }
        return "unknown";
    }
}
