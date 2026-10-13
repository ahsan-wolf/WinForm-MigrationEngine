using WebFormsMigrator.IR.Models;

namespace Migrator.Engine.Support;

/// <summary>
/// Classes of the legacy project that are not forms or user controls (static helper modules, data classes, enums, structs).
/// A WinForms-owned sidecar of the shared IR (<c>ir/support.ir.json</c>): the shared PageIr models only UI units, and CLAUDE.md §2.2 forbids
/// changing it for this. Member shapes reuse <see cref="MemberIr"/>; see docs/ENGINE_AUTHORING.md for the conventions.
/// </summary>
public sealed class SupportFileIr
{
    public string SchemaVersion { get; set; } = IrSchema.Version;
    public List<SupportClassIr> Classes { get; set; } = new();
}

public sealed class SupportClassIr
{
    public string Name { get; set; } = "";

    /// <summary><c>class</c>, <c>struct</c> or <c>enum</c>.</summary>
    public string Kind { get; set; } = "class";
    public bool IsStatic { get; set; }

    /// <summary>Enclosing class for nested types (the type is still addressed by its simple name).</summary>
    public string? Container { get; set; }
    public string? BaseType { get; set; }
    public string RelativePath { get; set; } = "";

    /// <summary>True for a class of a referenced project outside the analysed folder: a library, used for its shape (types, members), not translated.</summary>
    public bool External { get; set; }

    /// <summary>
    /// Kind Field/Property/Method. <c>Body</c>: field/auto-property = initializer expression; method = full body; property with accessors = the accessor
    /// list text <c>{ get {...} set {...} }</c> (expression-bodied properties are normalised to it). <c>Attributes</c> carries modifiers as
    /// <c>mod:const</c>, <c>mod:readonly</c>, <c>mod:auto</c>, <c>mod:accessors</c>, <c>mod:async</c>; constructors are methods named <c>.ctor</c>.
    /// </summary>
    public List<MemberIr> Members { get; set; } = new();

    public List<SupportEnumValueIr> EnumValues { get; set; } = new();

    /// <summary>Constructs that were not extracted (events, indexers, operators, delegates): never silently dropped.</summary>
    public List<string> Unsupported { get; set; } = new();
}

public sealed class SupportEnumValueIr
{
    public string Name { get; set; } = "";

    /// <summary>The initializer expression text, or null when implicit.</summary>
    public string? Value { get; set; }
}
