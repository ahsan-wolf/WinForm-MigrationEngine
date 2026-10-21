using Migrator.Engine.Contracts;

namespace Migrator.Target.Core.Generation;

/// <summary>One generated unit: the markup file, the code-behind file, the legacy copy and what the report needs. Shared by every target.</summary>
public sealed class GeneratedPage
{
    public string ComponentName { get; init; } = "";
    public string Source { get; init; } = "";

    /// <summary>The code-behind file (`&lt;Component&gt;.code.tsx`): model, handlers, translated methods and the event wiring table. Empty for visual-only pages. `MethodRanges` refer to this file.</summary>
    public string CodeSource { get; init; } = "";

    /// <summary>The component class file, for frameworks that keep one besides the markup and the code-behind (Angular). Empty otherwise.</summary>
    public string ComponentSource { get; init; } = "";
    public string LegacySource { get; init; } = "";
    public List<GenerationIssue> Issues { get; } = new();
    public int Controls { get; init; }
    public int Handlers { get; init; }
    public int NavigationHandlers { get; init; }
    public List<string> NonVisual { get; init; } = new();

    /// <summary>Icon files this page needs, as (path relative to the app folder, bytes): `public/icons/&lt;unit&gt;/&lt;control&gt;.png`.</summary>
    public List<(string Path, byte[] Data)> Assets { get; init; } = new();
    public int IconsExtracted { get; init; }
    public int IconsConverted { get; init; }
    public int IconsPlaceholder { get; init; }

    // Translation (rules) results; all zero when translation is off.
    public int MethodsTotal { get; set; }
    public int MethodsComplete { get; set; }
    public int MethodsPartial { get; set; }
    public int MethodsNotTranslated { get; set; }
    public int MethodsDemoted { get; set; }
    public List<string> TodoReasons { get; } = new();
    public List<string> Bypassed { get; } = new();

    /// <summary>Line ranges (1-based, inclusive) of each translated function in Source; the compile gate maps TypeScript errors back to methods through them.</summary>
    public List<(string TsName, int Start, int End)> MethodRanges { get; } = new();
}
