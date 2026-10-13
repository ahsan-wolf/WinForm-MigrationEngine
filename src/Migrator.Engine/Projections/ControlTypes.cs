using WebFormsMigrator.IR.Models;

namespace Migrator.Engine.Projections;

/// <summary>
/// A control's type as targets should see it. Custom controls (a project's own `FancyTextBox : TextBox`, a vendor's `Vendor.Grid : DataGridView`)
/// keep their declared type in <c>TagName</c>; when the source engine could follow the base-type chain to a standard framework type it also
/// records that in the attribute <see cref="ResolvedTypeKey"/>. Generators map by <see cref="Effective"/>, never by name patterns.
/// </summary>
public static class ControlTypes
{
    public const string ResolvedTypeKey = "ResolvedType";

    /// <summary>The standard framework type behind the control when known, else the declared type.</summary>
    public static string Effective(this ControlNodeIr node) =>
        node.Attributes.TryGetValue(ResolvedTypeKey, out var resolved) && resolved.Length > 0 ? resolved : node.TagName;
}
