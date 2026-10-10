using System.Linq;

namespace WebFormsMigrator.WinFormsAnalyzers
{
    /// <summary>
    /// Same idea as the naming-keyword heuristics described for
    /// NavigationWorkflowExtractor.SemanticKeywords (PROJECT_DOCUMENTATION.md §2.3),
    /// applied here to WinForms control Ids / button Text / method names instead of
    /// aspx control ids. First matching keyword wins; order = priority, most
    /// specific/business-relevant first. Per the "Pluggable semantic-keyword
    /// configuration" backlog idea (§8), consider externalizing this table to
    /// JSON/YAML rather than hardcoding it, so teams can tune it per codebase.
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

        /// <summary>
        /// Returns the first matching semantic across all candidate strings
        /// (e.g. control Id, then button Text, then method name), or null if
        /// nothing matches — matching the observed sample behavior where
        /// e.g. ValidateForm has no inferred Semantic.
        /// </summary>
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
}
