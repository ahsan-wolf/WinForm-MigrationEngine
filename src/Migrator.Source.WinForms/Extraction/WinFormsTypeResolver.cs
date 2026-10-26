namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Follows a custom control's base-type chain (through every class of the project, including class libraries) to the standard
/// <c>System.Windows.Forms</c> type it derives from. Purely structural: no class, namespace or assembly name of any application appears here.
/// </summary>
public sealed class WinFormsTypeResolver
{
    private const string Namespace = "System.Windows.Forms.";

    /// <summary>Standard WinForms controls and components a project class can derive from (framework API knowledge, not application knowledge).</summary>
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "Button", "CheckBox", "CheckedListBox", "ComboBox", "DataGridView", "DateTimePicker", "DomainUpDown", "FlowLayoutPanel", "Form", "GroupBox",
        "HScrollBar", "Label", "LinkLabel", "ListBox", "ListView", "MaskedTextBox", "MenuStrip", "MonthCalendar", "NumericUpDown", "Panel", "PictureBox",
        "ProgressBar", "PropertyGrid", "RadioButton", "RichTextBox", "SplitContainer", "StatusStrip", "TabControl", "TabPage", "TableLayoutPanel",
        "TextBox", "ToolStrip", "ToolStripButton", "ToolStripComboBox", "ToolStripDropDownButton", "ToolStripLabel", "ToolStripMenuItem",
        "ToolStripProgressBar", "ToolStripSeparator", "ToolStripSplitButton", "ToolStripStatusLabel", "ToolStripTextBox", "TrackBar", "TreeView",
        "UserControl", "VScrollBar", "WebBrowser", "ContextMenuStrip", "BindingNavigator", "Control", "ScrollableControl", "ContainerControl",
        "ListViewItem", "ColumnHeader", "TreeNode", "ImageList", "ToolTip", "BindingSource", "Timer", "ErrorProvider", "OpenFileDialog",
        "SaveFileDialog", "FolderBrowserDialog", "ColorDialog", "FontDialog", "NotifyIcon", "BackgroundWorker", "HelpProvider", "PrintDocument",
        "PrintDialog", "PageSetupDialog", "DataGridViewTextBoxColumn", "DataGridViewCheckBoxColumn", "DataGridViewComboBoxColumn",
        "DataGridViewButtonColumn", "DataGridViewImageColumn", "DataGridViewLinkColumn", "DataGridViewColumn",
        // pre-ToolStrip and pre-DataGridView controls (old projects, decompiled sources)
        "MainMenu", "MenuItem", "ContextMenu", "DataGrid", "DataGridTextBoxColumn", "DataGridBoolColumn", "DataGridTableStyle", "StatusBar", "ToolBar", "Splitter"
    };

    private readonly IReadOnlyDictionary<string, string> _bases;

    public WinFormsTypeResolver(IReadOnlyDictionary<string, string>? typeBases) =>
        _bases = typeBases ?? new Dictionary<string, string>();

    private static string Simple(string type)
    {
        var generic = type.IndexOf('<');
        if (generic >= 0) type = type[..generic];
        return type[(type.LastIndexOf('.') + 1)..].Trim();
    }

    /// <summary>
    /// The standard framework type a custom type derives from (<c>System.Windows.Forms.TextBox</c>), or null when the chain ends elsewhere
    /// (a third-party base without source, such as a report viewer). A type that is already a standard type resolves to itself.
    /// </summary>
    public string? Resolve(string typeName)
    {
        if (typeName.StartsWith(Namespace, StringComparison.Ordinal)) return typeName;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = Simple(typeName);
        while (seen.Add(current))
        {
            if (!_bases.TryGetValue(current, out var baseType))
                return Known.Contains(current) && typeName != current ? Namespace + current : null;

            // `: System.Windows.Forms.Label` names the framework type outright.
            if (baseType.StartsWith(Namespace, StringComparison.Ordinal)) return Namespace + Simple(baseType);
            var next = Simple(baseType);
            // `class Label : Label` (a project class named like the framework type it derives from) can only mean the framework type.
            if (Known.Contains(next) && (next == current || !_bases.ContainsKey(next))) return Namespace + next;
            current = next;
        }
        return null;
    }

    /// <summary>
    /// A framework control written without its namespace (`new TextBox()` in a file that has `using System.Windows.Forms;`, as decompiled and single-file sources do)
    /// is qualified like the designer files of Visual Studio write it, unless the project declares a class of that name.
    /// </summary>
    public string Qualify(string typeName) =>
        !typeName.Contains('.') && !typeName.Contains('<') && Known.Contains(typeName) && !_bases.ContainsKey(typeName) ? Namespace + typeName : typeName;

    /// <summary>True when the chain reaches a standard control other than a Form (a reusable control: user control or derived control).</summary>
    public bool IsControlLike(string typeName) =>
        Resolve(typeName) is { } resolved && resolved != Namespace + "Form";
}
