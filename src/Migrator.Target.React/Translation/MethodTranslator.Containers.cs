using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.React.Translation;

internal sealed partial class MethodTranslator
{
    // ------------------------------------------------------------------------------------------------ image lists

    /// <summary>`Properties.Resources.Name` (or `Resources.Name`): the web path of the image file written for it.</summary>
    private string? ResourceImage(MemberAccessExpressionSyntax ma)
    {
        var isResources = ma.Expression is IdentifierNameSyntax { Identifier.ValueText: "Resources" } && !IsKnownName("Resources")
                          || ma.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Resources", Expression: IdentifierNameSyntax { Identifier.ValueText: "Properties" } };
        if (!isResources) return null;
        var name = ma.Name.Identifier.ValueText;
        if (_scope.Resources.WebPath(name) is not { } path)
            throw new Untranslatable($"resource {name} is not an image the engine extracted (string and file resources are not read; --images <folder> can supply images by name)");
        _scope.Output.ResourceRefs.Add(name);
        return Str(path);
    }

    private string? ImageListOf(ExpressionSyntax e) =>
        e is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Images" } m && ControlOf(m.Expression) is { } id && _scope.ImageLists.Contains(id) ? id : null;

    /// <summary>`il.Images.Count` / `Empty`</summary>
    private string? ImageListMember(MemberAccessExpressionSyntax ma)
    {
        if (ma.Name.Identifier.ValueText is not ("Count" or "Empty") || ImageListOf(ma.Expression) is not { } id) return null;
        _scope.Output.UsesViewModel = true;
        return ma.Name.Identifier.ValueText == "Count" ? $"vm.{id}.urls.length" : $"(vm.{id}.urls.length === 0)";
    }

    /// <summary>
    /// `il.Images.Add / Clear / RemoveAt / RemoveByKey / ContainsKey / IndexOfKey / SetKeyName` over the image list's keys and urls. An image argument that cannot be
    /// resolved (a project resource, a file) is added as a placeholder image, with a TODO.
    /// </summary>
    private string? ImageListMethod(ExpressionSyntax receiver, string method, InvocationExpressionSyntax inv)
    {
        if (ImageListOf(receiver) is not { } id) return null;
        var args = inv.ArgumentList.Arguments;
        _scope.Output.UsesViewModel = true;
        string Arg(int i) => Ex(args[i].Expression);

        switch (method, args.Count)
        {
            case ("Add", 2) when args[1].Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "Color" } }:
                return $"wfImageListAdd(vm.{id}, {Arg(0)}, {ImageValue(args[1].Expression)})";
            case ("Add", 1 or 2): return $"wfImageListAdd(vm.{id}, \"\", {ImageValue(args[0].Expression)})";
            case ("Clear", 0): return $"wfImageListClear(vm.{id})";
            case ("RemoveAt", 1): return $"wfImageListRemoveAt(vm.{id}, {Arg(0)})";
            case ("RemoveByKey", 1): return $"wfImageListRemoveByKey(vm.{id}, {Arg(0)})";
            case ("ContainsKey", 1): return $"vm.{id}.keys.includes({Arg(0)})";
            case ("IndexOfKey", 1): return $"vm.{id}.keys.indexOf({Arg(0)})";
            case ("SetKeyName", 2): return $"(vm.{id}.keys[{Arg(0)}] = {Arg(1)})";
        }
        throw new Untranslatable($"call to {id}.Images.{method}(...) is not translated");
    }

    private string ImageValue(ExpressionSyntax image)
    {
        try
        {
            return Ex(image);
        }
        catch (Untranslatable)
        {
            _after.Add($"// TODO: image {Collapse(image)} is not resolvable from IR; a placeholder image is added in its place");
            return "wfPlaceholderImage()";
        }
    }

    // ------------------------------------------------------------------------------------------------- tab pages

    private string? TabOf(ExpressionSyntax e) => ControlOf(e) is { } id && _scope.TabPages.ContainsKey(id) ? id : null;

    private string? TabPageControl(ExpressionSyntax e) => ControlOf(e) is { } id && _scope.TabPageParent.ContainsKey(id) ? id : null;

    /// <summary>The model entries of a tab control's pages, in designer order: `[vm.tpA, vm.tpB]`.</summary>
    private string TabPageModels(string tab) => "[" + string.Join(", ", _scope.TabPages[tab].Select(p => $"vm.{p}")) + "]";

    private string ShownTabPages(string tab) => $"{TabPageModels(tab)}.filter((p) => p.visible)";

    private bool IsSelectedTab(ExpressionSyntax e, out string tab)
    {
        tab = "";
        if (e is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SelectedTab" } m || TabOf(m.Expression) is not { } id) return false;
        tab = id;
        return true;
    }

    /// <summary>`tab.TabPages.Count` / `tab.TabCount`: the pages that are shown.</summary>
    private string? TabCount(MemberAccessExpressionSyntax ma)
    {
        var name = ma.Name.Identifier.ValueText;
        var tab = name == "TabCount" ? TabOf(ma.Expression)
            : name == "Count" && ma.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "TabPages" } pages ? TabOf(pages.Expression) : null;
        if (tab is null) return null;
        _scope.Output.UsesViewModel = true;
        return $"{ShownTabPages(tab)}.length";
    }

    /// <summary>`tab.SelectedTab.Name` / `.Text`: read through the selected index.</summary>
    private string? SelectedTabMember(ExpressionSyntax left, string name)
    {
        if (!IsSelectedTab(left, out var tab) || name is not ("Name" or "Text")) return null;
        _scope.Output.UsesViewModel = true;
        var values = name == "Name"
            ? "[" + string.Join(", ", _scope.TabPages[tab].Select(Str)) + "]"
            : "[" + string.Join(", ", _scope.TabPages[tab].Select(p => $"vm.{p}.text")) + "]";
        return $"{values}[vm.{tab}.selectedIndex]";
    }

    /// <summary>`tab.SelectedTab == tpPage` / `!=`: the selected index against the page's position.</summary>
    private bool TryTabCompare(BinaryExpressionSyntax b, out string result)
    {
        result = "";
        var (selected, other) = IsSelectedTab(b.Left, out _) ? (b.Left, b.Right) : IsSelectedTab(b.Right, out _) ? (b.Right, b.Left) : (null, null);
        if (selected is null || other is null || !IsSelectedTab(selected, out var tab)) return false;
        if (TabPageControl(other) is not { } page || _scope.TabPageParent[page] != tab) return false;
        _scope.Output.UsesViewModel = true;
        result = $"vm.{tab}.selectedIndex {(b.OperatorToken.Text == "!=" ? "!==" : "===")} {_scope.TabPages[tab].IndexOf(page)}";
        return true;
    }

    /// <summary>`tab.SelectedTab = tpPage` selects the page by index.</summary>
    private bool TryTabAssign(AssignmentExpressionSyntax a, out string result)
    {
        result = "";
        var tab = "";
        if (!a.IsKind(SyntaxKind.SimpleAssignmentExpression) || !IsSelectedTab(a.Left, out tab)) return false;
        if (TabPageControl(a.Right) is not { } page || _scope.TabPageParent[page] != tab) return false;
        _scope.Output.UsesViewModel = true;
        result = $"vm.{tab}.selectedIndex = {_scope.TabPages[tab].IndexOf(page)}";
        return true;
    }

    /// <summary>`tab.TabPages.Add / Insert / Remove / RemoveAt / Clear / Contains / IndexOf`: pages are fixed at design time, so Add and Remove show and hide them.</summary>
    private string? TabPagesMethod(ExpressionSyntax receiver, string method, InvocationExpressionSyntax inv)
    {
        if (receiver is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "TabPages" } m || TabOf(m.Expression) is not { } tab) return null;
        var args = inv.ArgumentList.Arguments;
        _scope.Output.UsesViewModel = true;
        string? Page(int i) => args.Count > i && TabPageControl(args[i].Expression) is { } p && _scope.TabPageParent[p] == tab ? p : null;

        switch (method, args.Count)
        {
            case ("Add", 1) when Page(0) is { } page: return $"vm.{page}.visible = true";
            case ("Insert", 2) when Page(1) is { } page: return $"vm.{page}.visible = true";
            case ("Remove", 1) when Page(0) is { } page: return $"vm.{page}.visible = false";
            case ("RemoveAt", 1): return $"{ShownTabPages(tab)}[{Ex(args[0].Expression)}].visible = false";
            case ("Clear", 0): return $"{TabPageModels(tab)}.forEach((p) => {{ p.visible = false; }})";
            case ("Contains", 1) when Page(0) is { } page: return $"vm.{page}.visible";
            case ("IndexOf", 1) when Page(0) is { } page: return _scope.TabPages[tab].IndexOf(page).ToString();
        }
        throw new Untranslatable($"call to {tab}.TabPages.{method}(...) is not translated (pages are fixed at design time; Add/Remove show and hide them)");
    }

    // --------------------------------------------------------------------------------------- controls added in code

    private string? DynamicHost(ExpressionSyntax e) => ControlOf(e) is { } id && _scope.DynamicHosts.Contains(id) ? id : null;

    /// <summary>`panel.Controls.Count`</summary>
    private string? HostCount(MemberAccessExpressionSyntax ma)
    {
        if (ma.Name.Identifier.ValueText != "Count" || ma.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Controls" } c || DynamicHost(c.Expression) is not { } host) return null;
        _scope.Output.UsesViewModel = true;
        return $"vm.{host}.controls.length";
    }

    /// <summary>`panel.Controls.Add(new SomeUserControl(args)) / Clear / RemoveAt` over the host's `controls` list.</summary>
    private string? HostMethod(ExpressionSyntax receiver, string method, InvocationExpressionSyntax inv)
    {
        if (receiver is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Controls" } m || DynamicHost(m.Expression) is not { } host) return null;
        var args = inv.ArgumentList.Arguments;
        _scope.Output.UsesViewModel = true;
        switch (method, args.Count)
        {
            case ("Add", 1): return $"vm.{host}.controls.push({DynamicControl(args[0].Expression)})";
            case ("Clear", 0): return $"vm.{host}.controls.splice(0)";
            case ("RemoveAt", 1): return $"vm.{host}.controls.splice({Ex(args[0].Expression)}, 1)";
        }
        throw new Untranslatable($"call to {host}.Controls.{method}(...) is not translated");
    }

    /// <summary>`new SomeUserControl(a, b)` as a list entry: the component and its constructor arguments as props (by parameter name).</summary>
    private string DynamicControl(ExpressionSyntax e)
    {
        if (e is not ObjectCreationExpressionSyntax oc) throw new Untranslatable("control added to a container is not a `new UserControl(...)` expression");
        var type = SimpleTypeName(oc.Type);
        if (!_scope.Units.TryGetValue(type, out var unit) || !unit.IsUserControl)
            throw new Untranslatable($"{type} added to a container (only project user controls can be created at run time)");
        if (!_scope.Generated.Contains(type)) throw new Untranslatable($"user control {type} is outside the generated scope");

        var component = _scope.Components[type];
        var args = oc.ArgumentList?.Arguments ?? default;
        var ctors = unit.Page.Members.Where(x => x.Kind == MemberKind.Method && x.Name == ".ctor" && x.Parameters.Count > 0).ToList();
        var widest = ctors.OrderByDescending(x => x.Parameters.Count).FirstOrDefault();
        _scope.Output.PageImports.Add(component);
        if (args.Count == 0) return $"wfDynamicControl({component})";

        // The component's props are the parameters of the widest constructor; an overload maps its own parameter names onto them.
        var chosen = ctors.FirstOrDefault(x => x.Parameters.Count == args.Count);
        if (chosen is null || widest is null || chosen.Parameters.Any(p => widest.Parameters.All(w => w.Name != p.Name)))
            throw new Untranslatable($"constructor of {type} with {args.Count} argument(s) is not available");
        var props = args.Select((a, i) => $"{chosen.Parameters[i].Name}: {Ex(a.Expression)}");
        return $"wfDynamicControl({component}, {{ {string.Join(", ", props)} }})";
    }
}
