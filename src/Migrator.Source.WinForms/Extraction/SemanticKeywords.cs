namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Naming-keyword heuristics for control Ids / button Text / method names. First matching keyword wins;
/// order = priority, most specific first. TODO: externalize to JSON so teams can tune it per codebase.
/// </summary>
public static class SemanticKeywords
{
    public static readonly (string[] Keywords, string Semantic)[] Map =
    {
        (new[] { "search", "filter", "find" }, "Filters or searches the data set"),
        (new[] { "clear", "reset" }, "Resets input fields"),
        (new[] { "cancel" }, "Discards changes and resets the form"),
        (new[] { "delete", "remove" }, "Removes a record"),
        (new[] { "edit", "modify" }, "Enters edit mode for a selected record"),
        (new[] { "save", "submit", "persist" }, "Persists form data (create or update)"),
        (new[] { "insert", "create" }, "Creates a new record"),
        (new[] { "update" }, "Updates an existing record"),
        (new[] { "load", "bind", "fill", "refresh", "populate" }, "Loads and displays data"),
        (new[] { "close", "exit", "quit" }, "Closes the form or application"),
        (new[] { "print" }, "Prints the current view"),
        (new[] { "export" }, "Exports data to a file"),
    };

    /// <summary>First matching semantic across all candidates (e.g. control Id, then Text), or null.</summary>
    public static string? Infer(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var lower = candidate.ToLowerInvariant();
            foreach (var (keywords, semantic) in Map)
            {
                if (keywords.Any(lower.Contains))
                    return semantic;
            }
        }
        return null;
    }
}
