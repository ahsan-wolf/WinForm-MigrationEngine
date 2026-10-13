using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Migrator.Source.Backend;

/// <summary>
/// Read-only survey of .NET Framework backend code (class libraries, ASMX/WCF services) and WSDL files. Syntax only: nothing is compiled or run.
/// What counts as Windows-specific, host-bound or configuration access comes from <see cref="BackendPatterns"/> (data), never from application names.
/// </summary>
public sealed class BackendAnalyzer
{
    private static readonly string[] SkippedDirectories = { "bin", "obj", ".git", ".vs", "node_modules", "packages" };
    private static readonly string[] CollectionTypes = { "List<", "Dictionary<", "Hashtable", "ArrayList", "DataTable", "DataSet", "HashSet<", "Queue<", "Stack<", "SortedList" };

    private readonly BackendPatterns _rules;
    private readonly Regex _windows, _config, _host, _data;

    public BackendAnalyzer(BackendPatterns rules)
    {
        _rules = rules;
        _windows = Names(rules.WindowsNames.Concat(rules.WindowsNamespaces));
        _config = Names(rules.ConfigAccess);
        _host = Names(rules.HostContext);
        _data = Names(rules.DataAccessTypes);
    }

    public static BackendPatterns LoadRules(string? path)
    {
        if (path is null || !File.Exists(path)) return new BackendPatterns();
        return JsonSerializer.Deserialize<BackendPatterns>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new BackendPatterns();
    }

    private static Regex Names(IEnumerable<string> names)
    {
        var parts = names.Where(n => n.Length > 0).Select(Regex.Escape).ToList();
        return new Regex(parts.Count == 0 ? "(?!)" : @"(?<![\w])(" + string.Join("|", parts) + @")(?![\w])", RegexOptions.Compiled);
    }

    public BackendAnalysis Analyze(IEnumerable<string> sourceRoots, IEnumerable<string> wsdlFolders)
    {
        var result = new BackendAnalysis();
        var parsed = new List<(string File, SyntaxNode Root)>();

        foreach (var root in sourceRoots)
        {
            if (!Directory.Exists(root)) { result.Warnings.Add($"Source folder not found: {root}"); continue; }
            var cs = Enumerate(root, "*.cs").ToList();
            var classes = 0;
            foreach (var file in cs)
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
                parsed.Add((file, tree));
                classes += tree.DescendantNodes().OfType<ClassDeclarationSyntax>().Count();
            }
            var configs = Enumerate(root, "*.config").Where(f => Path.GetFileName(f).StartsWith("web", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).StartsWith("app", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var c in configs) ReadConfig(c, result);
            result.Sources.Add(new SourceSummary(root, cs.Count, classes, Enumerate(root, "*.asmx").Count(), configs.Count));
        }

        foreach (var folder in wsdlFolders)
        {
            if (!Directory.Exists(folder)) { result.Warnings.Add($"WSDL folder not found: {folder}"); continue; }
            foreach (var file in Enumerate(folder, "*.wsdl")) ReadWsdl(file, result);
        }

        // Pass 1: classify every class (service, proxy, other).
        var proxyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (file, root) in parsed)
            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var kind = Classify(cls);
                if (kind == "proxy") proxyNames.Add(cls.Identifier.ValueText);
            }
        var allClasses = new HashSet<string>(parsed.SelectMany(p => p.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()).Select(c => c.Identifier.ValueText));

        // Pass 2: details.
        foreach (var (file, root) in parsed)
        {
            foreach (var u in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
                if (u.Name?.ToString() is { } ns && _rules.WindowsNamespaces.Any(w => ns == w || ns.StartsWith(w + ".", StringComparison.Ordinal)))
                    result.Findings.Add(new Finding("windows", ns, file, "(file)", "(using)", Line(u)));
            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var name = cls.Identifier.ValueText;
                var rel = file;
                CollectFindings(cls, rel, result);
                CollectStatic(cls, rel, result);

                var kind = Classify(cls);
                if (kind == "proxy") { MergeProxy(result, ReadProxy(cls, rel)); continue; }
                if (kind == "service") { result.Services.Add(ReadService(cls, rel, allClasses)); continue; }
                if (cls.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) && cls.Parent is not ClassDeclarationSyntax)
                {
                    var facade = ReadFacade(cls, rel, proxyNames);
                    if (facade.Methods > 0) result.Facades.Add(facade);
                }
            }
        }
        return result;
    }

    private string Classify(ClassDeclarationSyntax cls)
    {
        var bases = cls.BaseList?.Types.Select(t => Simple(t.Type.ToString())).ToList() ?? new List<string>();
        if (bases.Any(b => _rules.ProxyBaseTypes.Contains(b))) return "proxy";
        if (bases.Any(b => _rules.ServiceBaseTypes.Contains(b)) || cls.AttributeLists.SelectMany(a => a.Attributes).Any(a => _rules.ServiceAttributes.Contains(Simple(a.Name.ToString())))) return "service";
        return "other";
    }

    private ProxyInfo ReadProxy(ClassDeclarationSyntax cls, string file) => new(
        cls.Identifier.ValueText, file, cls.BaseList is null ? "(partial)" : Simple(cls.BaseList.Types.First().Type.ToString()),
        cls.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)) && !m.Identifier.ValueText.StartsWith("Begin") && !m.Identifier.ValueText.StartsWith("End") && !m.Identifier.ValueText.EndsWith("Async"))
            .Select(m => m.Identifier.ValueText).ToList(),
        cls.DescendantNodes().OfType<AssignmentExpressionSyntax>().Count(a => a.Left.ToString().EndsWith("Url", StringComparison.Ordinal)));

    /// <summary>Partial classes: one entry per class, operations and Url assignments summed, the base type taken from the part that declares it.</summary>
    private static void MergeProxy(BackendAnalysis result, ProxyInfo part)
    {
        var i = result.Proxies.FindIndex(p => p.Name == part.Name);
        if (i < 0) { result.Proxies.Add(part); return; }
        var o = result.Proxies[i];
        result.Proxies[i] = o with { BaseType = o.BaseType == "(partial)" ? part.BaseType : o.BaseType, Operations = o.Operations.Union(part.Operations).ToList(), UrlAssignments = o.UrlAssignments + part.UrlAssignments };
    }

    private ServiceInfo ReadService(ClassDeclarationSyntax cls, string file, HashSet<string> allClasses)
    {
        var operations = new List<OperationInfo>();
        foreach (var m in cls.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!m.AttributeLists.SelectMany(a => a.Attributes).Any(a => _rules.MethodAttributes.Contains(Simple(a.Name.ToString())))) continue;
            var text = m.Body?.ToString() ?? m.ExpressionBody?.ToString() ?? "";
            var calls = Regex.Matches(text, @"\b([A-Za-z_]\w*)\s*\.\s*[A-Za-z_]\w*\s*\(").Select(x => x.Groups[1].Value).Where(allClasses.Contains).Where(n => n != cls.Identifier.ValueText).Distinct().ToList();
            operations.Add(new OperationInfo(m.Identifier.ValueText, m.ReturnType.ToString(), m.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier}").ToList(),
                CountStatements(m), calls, _data.IsMatch(text), _windows.IsMatch(text), _host.IsMatch(text), _config.IsMatch(text)));
        }
        return new ServiceInfo(cls.Identifier.ValueText, file, cls.BaseList is null ? "" : Simple(cls.BaseList.Types.First().Type.ToString()), operations);
    }

    private FacadeInfo ReadFacade(ClassDeclarationSyntax cls, string file, HashSet<string> proxyNames)
    {
        int methods = 0, thin = 0, logic = 0, data = 0, windows = 0;
        var proxies = new HashSet<string>();
        foreach (var m in cls.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword) || x.IsKind(SyntaxKind.InternalKeyword))))
        {
            methods++;
            var text = m.Body?.ToString() ?? m.ExpressionBody?.ToString() ?? "";
            var used = proxyNames.Where(p => Regex.IsMatch(text, @"\b" + Regex.Escape(p) + @"\b")).ToList();
            foreach (var p in used) proxies.Add(p);
            if (_data.IsMatch(text)) data++;
            if (_windows.IsMatch(text)) windows++;
            if (used.Count > 0 && CountStatements(m) <= 8) thin++; else logic++;
        }
        return new FacadeInfo(cls.Identifier.ValueText, file, cls.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)), methods, thin, logic, data, windows, proxies.OrderBy(x => x).ToList());
    }

    private void CollectStatic(ClassDeclarationSyntax cls, string file, BackendAnalysis result)
    {
        var name = cls.Identifier.ValueText;
        foreach (var f in cls.Members.OfType<FieldDeclarationSyntax>())
        {
            if (!f.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) || f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword))) continue;
            var readOnly = f.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword));
            var type = f.Declaration.Type.ToString();
            if (readOnly && !CollectionTypes.Any(type.Contains)) continue; // an immutable value or a reference that is never reassigned
            foreach (var v in f.Declaration.Variables)
                result.StaticState.Add(new StaticStateInfo(name, v.Identifier.ValueText, type, readOnly ? "static readonly collection" : "static field", file, Line(v)));
        }
        foreach (var p in cls.Members.OfType<PropertyDeclarationSyntax>())
            if (p.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) && p.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) == true)
                result.StaticState.Add(new StaticStateInfo(name, p.Identifier.ValueText, p.Type.ToString(), "static property with setter", file, Line(p)));
    }

    private void CollectFindings(ClassDeclarationSyntax cls, string file, BackendAnalysis result)
    {
        var className = cls.Identifier.ValueText;
        var seen = new HashSet<(string, string, string, int)>();
        void Add(string category, SyntaxNode node, string text)
        {
            var regex = category switch { "windows" => _windows, "config" => _config, "host-context" => _host, _ => _data };
            foreach (Match match in regex.Matches(text))
            {
                var member = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault(m => m is not ClassDeclarationSyntax) is { } md ? MemberName(md) : "(class)";
                var line = Line(node);
                if (seen.Add((category, match.Value, member, line))) result.Findings.Add(new Finding(category, match.Value, file, className, member, line));
            }
        }

        foreach (var node in cls.DescendantNodes())
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax ma when ma.Parent is not MemberAccessExpressionSyntax pm || pm.Expression != ma:
                    var chain = ma.ToString();
                    if (chain.Length > 120) chain = chain[..120];
                    foreach (var c in new[] { "windows", "config", "host-context", "data-access" }) Add(c, ma, chain);
                    break;
                case ObjectCreationExpressionSyntax oc:
                    Add("windows", oc, oc.Type.ToString()); Add("data-access", oc, oc.Type.ToString());
                    break;
                case VariableDeclarationSyntax vd:
                    Add("windows", vd, vd.Type.ToString()); Add("data-access", vd, vd.Type.ToString());
                    break;
                case AttributeSyntax at when _rules.WindowsAttributes.Contains(Simple(at.Name.ToString())):
                    result.Findings.Add(new Finding("windows", Simple(at.Name.ToString()), file, className, MemberName(at.Ancestors().OfType<MemberDeclarationSyntax>().First()), Line(at)));
                    break;
                case ElementAccessExpressionSyntax ea:
                    Add("host-context", ea, ea.Expression.ToString() + "[");
                    break;
            }
        }
    }

    private static void ReadConfig(string file, BackendAnalysis result)
    {
        try
        {
            var doc = XDocument.Load(file);
            var app = doc.Descendants("appSettings").Descendants("add").Select(e => (string?)e.Attribute("key")).OfType<string>().ToList();
            var cs = doc.Descendants("connectionStrings").Descendants("add").Select(e => (string?)e.Attribute("name")).OfType<string>().ToList();
            if (app.Count + cs.Count > 0) result.Config.Add(new ConfigKeys(file, app, cs)); // names only, never values
        }
        catch (System.Xml.XmlException) { result.Warnings.Add($"Not readable as XML: {file}"); }
    }

    private static void ReadWsdl(string file, BackendAnalysis result)
    {
        try
        {
            var doc = XDocument.Load(file);
            string?[] Names(string element, string attribute) => doc.Descendants().Where(e => e.Name.LocalName == element).Select(e => (string?)e.Attribute(attribute)).ToArray();
            var ops = doc.Descendants().Where(e => e.Name.LocalName == "portType").SelectMany(p => p.Elements().Where(e => e.Name.LocalName == "operation")).Select(e => (string?)e.Attribute("name")).OfType<string>().Distinct().ToList();
            var endpoints = doc.Descendants().Where(e => e.Name.LocalName == "address").Select(e => (string?)e.Attribute("location")).OfType<string>().Distinct().ToList();
            result.Wsdls.Add(new WsdlInfo(file, Names("service", "name").OfType<string>().ToList(), endpoints, ops));
        }
        catch (System.Xml.XmlException) { result.Warnings.Add($"Not readable as XML: {file}"); }
    }

    private static int CountStatements(MethodDeclarationSyntax m) => m.DescendantNodes().OfType<StatementSyntax>().Count(s => s is not BlockSyntax) + (m.ExpressionBody is null ? 0 : 1);
    private static string MemberName(MemberDeclarationSyntax m) => m switch
    {
        MethodDeclarationSyntax md => md.Identifier.ValueText,
        ConstructorDeclarationSyntax => ".ctor",
        PropertyDeclarationSyntax pd => pd.Identifier.ValueText,
        FieldDeclarationSyntax fd => fd.Declaration.Variables.First().Identifier.ValueText,
        _ => m.Kind().ToString()
    };
    private static int Line(SyntaxNode n) => n.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    private static string Simple(string name) { var i = name.LastIndexOf('.'); var s = i >= 0 ? name[(i + 1)..] : name; var g = s.IndexOf('<'); if (g >= 0) s = s[..g]; return s.EndsWith("Attribute", StringComparison.Ordinal) ? s[..^"Attribute".Length] : s; }

    private static IEnumerable<string> Enumerate(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar)[..^1].Any(seg => SkippedDirectories.Contains(seg, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
}
