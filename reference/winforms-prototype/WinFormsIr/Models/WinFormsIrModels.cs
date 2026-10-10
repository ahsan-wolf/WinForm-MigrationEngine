using System.Collections.Generic;

namespace WebFormsMigrator.IR.Models
{
    // NOTE: If your solution already has WebFormsMigrator.IR.Models with these
    // types (PageIr, ControlNodeIr, MemberIr, WorkflowIr, ...), DELETE this file
    // and add `using WebFormsMigrator.IR.Models;` to the Extraction/*.cs files
    // instead. This file only exists so the WinForms extractor is drop-in
    // runnable on its own, matching the field names/casing seen in
    // complete.ir.json and code-behind.ir.json.

    /// <summary>
    /// Kind mapping is inferred from the sample IR files (0 = property-like member,
    /// 2 = method). No Kind=1/Field/Event example appeared in the samples provided,
    /// so this enum's Field/Event slots are a best guess — reconcile against the
    /// real MemberIr.Kind enum when you wire this into WebFormsMigrator.IR.Models.
    /// </summary>
    public enum MemberKind
    {
        Property = 0,
        Field = 1,
        Method = 2,
        Event = 3
    }

    public enum MemberVisibility
    {
        Public = 1,
        Private = 2
    }

    public class ParameterIr
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string? DefaultValue { get; set; }
    }

    public class MemberIr
    {
        public int Kind { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int Visibility { get; set; }
        public bool IsStatic { get; set; }
        public List<ParameterIr> Parameters { get; set; } = new();
        public string? Body { get; set; }
    }

    public class ControlNodeIr
    {
        public string Id { get; set; } = "";
        public string TagName { get; set; } = "";
        public Dictionary<string, string> Attributes { get; set; } = new();
        public List<ControlNodeIr> Children { get; set; } = new();
        public string? RawContent { get; set; }
        public string? Semantic { get; set; }
    }

    public class CssAssetIr
    {
        public string Path { get; set; } = "";
    }

    public class JsAssetIr
    {
        public string Path { get; set; } = "";
    }

    public class NavigationIr
    {
        public string TriggerControlId { get; set; } = "";
        public string TriggerEvent { get; set; } = "";
        public string? HandlerMethod { get; set; }
        public string TargetForm { get; set; } = "";
        /// <summary>"Show" | "ShowDialog" | "Close" | "Hide" | "Exit"</summary>
        public string Kind { get; set; } = "";
    }

    public class WorkflowStepIr
    {
        public string MethodName { get; set; } = "";
        public string? Semantic { get; set; }
    }

    public class WorkflowIr
    {
        public string Name { get; set; } = "";
        public string TriggerControlId { get; set; } = "";
        public string TriggerEvent { get; set; } = "";
        public string HandlerMethod { get; set; } = "";
        public List<WorkflowStepIr> Steps { get; set; } = new();
        public string? Semantic { get; set; }
    }

    public class PageIr
    {
        public string SchemaVersion { get; set; } = "0.1.0";
        public string Name { get; set; } = "";
        public string? MasterPageName { get; set; }
        public ControlNodeIr RootControl { get; set; } = new();
        public List<MemberIr> Members { get; set; } = new();
        public List<CssAssetIr> CssAssets { get; set; } = new();
        public List<JsAssetIr> JsAssets { get; set; } = new();
        public List<NavigationIr> Navigations { get; set; } = new();
        public List<WorkflowIr> Workflows { get; set; } = new();
    }
}
