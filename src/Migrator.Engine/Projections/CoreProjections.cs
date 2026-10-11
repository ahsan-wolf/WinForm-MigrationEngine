using WebFormsMigrator.IR.Models;

namespace Migrator.Engine.Projections;

// Wrapper shapes mirror the Web Forms facet samples ({Name, RootControl} / {Name, Controls} / {Name, Members}).

public sealed record ControlSummary(string Name, string? Text, string ControlType, List<ControlSummary> Children);

public sealed record ControlsFile(string Name, ControlSummary? RootControl);

public sealed record ControlPropertiesEntry(string Id, string TagName, Dictionary<string, string> Attributes);

public sealed record ControlPropertiesFile(string Name, List<ControlPropertiesEntry> Controls);

public sealed record CodeBehindFile(string Name, List<MemberIr> Members);

/// <summary>Control tree only: Name, Text, ControlType, Children.</summary>
public sealed class ControlsProjection : IIrProjection
{
    public string FileSuffix => "controls";

    public object Project(PageIr page) => new ControlsFile(page.Name, page.RootControl is null ? null : Summarize(page.RootControl));

    private static ControlSummary Summarize(ControlNodeIr node) =>
        new(node.Id, node.Attributes.GetValueOrDefault("Text"), node.TagName, node.Children.Select(Summarize).ToList());
}

/// <summary>Flat depth-first list of every control with all its properties and event wiring.</summary>
public sealed class ControlPropertiesProjection : IIrProjection
{
    public string FileSuffix => "control-properties";

    public object Project(PageIr page)
    {
        var list = new List<ControlPropertiesEntry>();
        if (page.RootControl is not null) Flatten(page.RootControl, list);
        return new ControlPropertiesFile(page.Name, list);
    }

    private static void Flatten(ControlNodeIr node, List<ControlPropertiesEntry> into)
    {
        into.Add(new ControlPropertiesEntry(node.Id, node.TagName, new Dictionary<string, string>(node.Attributes)));
        foreach (var child in node.Children) Flatten(child, into);
    }
}

/// <summary>Members of the code-behind class (method bodies included).</summary>
public sealed class CodeBehindProjection : IIrProjection
{
    public string FileSuffix => "code-behind";

    public object Project(PageIr page) => new CodeBehindFile(page.Name, page.Members);
}
