namespace Migrator.Target.React.Translation;

/// <summary>
/// Statement-level demotion for the compile gate: when `tsc` rejects one simple statement of a translated method, only that statement becomes a
/// commented TODO and the rest of the method stays translated. Statements are addressed by their index among the method body's code lines
/// (comments and blank lines are not counted), measured on the method as the translator first produced it, so the address stays valid while the
/// gate demotes more lines. Anything that is not a standalone statement (blocks, headers, continuation lines) still demotes the whole method.
/// </summary>
internal static class LineDemotion
{
    public const int MaxPerMethod = 30;

    public static bool IsCode(string line) => line.Trim().Length > 0 && !line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    /// <summary>A line that can be commented out on its own without breaking the syntax around it.</summary>
    public static bool IsSimple(string line, string? previousCode)
    {
        var t = line.Trim();
        if (!t.EndsWith(';') || t.StartsWith('}') || t.StartsWith("//", StringComparison.Ordinal)) return false;
        if (previousCode is null) return true;
        var p = previousCode.TrimEnd();
        return !(p.EndsWith(',') || p.EndsWith('(') || p.EndsWith('+') || p.EndsWith("&&", StringComparison.Ordinal) || p.EndsWith("||", StringComparison.Ordinal)
                 || p.EndsWith('?') || p.EndsWith(':') || p.EndsWith('=') || t.StartsWith('.') || t.StartsWith(')'));
    }

    /// <summary>Index of the failing line among the code lines after the header, mapped back to the first-emission numbering given the lines already demoted.</summary>
    public static int ToOriginal(int currentIndex, IEnumerable<int> alreadyDemoted)
    {
        var original = currentIndex;
        foreach (var d in alreadyDemoted.OrderBy(x => x))
            if (d <= original) original++;
        return original;
    }

    /// <summary>The demoted code-line indices recorded for <paramref name="method"/> (keys look like `method#12`).</summary>
    public static SortedSet<int> For(IEnumerable<string> keys, string method)
    {
        var set = new SortedSet<int>();
        var prefix = method + "#";
        foreach (var key in keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(key[prefix.Length..], out var index)) set.Add(index);
        return set;
    }

    /// <summary>Comments out the demoted code lines of a freshly translated body.</summary>
    public static List<string> Apply(List<string> body, IReadOnlySet<int> indices, out int count)
    {
        count = 0;
        if (indices.Count == 0) return body;

        var result = new List<string>(body.Count);
        var code = 0;
        foreach (var line in body)
        {
            if (!IsCode(line)) { result.Add(line); continue; }
            if (indices.Contains(code))
            {
                var indent = line[..(line.Length - line.TrimStart().Length)];
                result.Add($"{indent}// TODO: not translated — this statement fails the TypeScript check: {line.Trim()}");
                count++;
            }
            else result.Add(line);
            code++;
        }
        return result;
    }
}
