namespace Migrator.Engine.Hosting;

public static class SchemaCompatibility
{
    /// <summary>
    /// Schema changes are additive only. Same major is required; while major is 0 the minor must match too
    /// (0.x is unstable); otherwise an engine built for an older-or-equal minor still works.
    /// </summary>
    public static bool IsCompatible(Version host, Version engine)
    {
        if (host.Major != engine.Major) return false;
        if (host.Major == 0) return host.Minor == engine.Minor;
        return engine.Minor <= host.Minor;
    }
}
