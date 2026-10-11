using System.Reflection;
using System.Runtime.Loader;
using Migrator.Engine.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Engine.Hosting;

public sealed class EngineLoadResult
{
    public EngineLoadResult(EngineRegistry registry, IReadOnlyList<EngineDiagnostic> diagnostics)
    {
        Registry = registry;
        Diagnostics = diagnostics;
    }

    public EngineRegistry Registry { get; }
    public IReadOnlyList<EngineDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// Builds an <see cref="EngineRegistry"/> from engines.json. Failure isolation: a broken engine is reported
/// as a diagnostic and skipped; it never aborts loading of the others.
/// </summary>
public static class EngineLoader
{
    public static EngineLoadResult Load(string enginesJsonPath)
    {
        var diagnostics = new List<EngineDiagnostic>();
        var registry = new EngineRegistry(Version.Parse(IrSchema.Version));

        EnginesConfig config;
        try
        {
            config = EnginesConfig.Load(enginesJsonPath);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new(DiagnosticSeverity.Error, $"Cannot read '{enginesJsonPath}': {ex.Message}"));
            return new EngineLoadResult(registry, diagnostics);
        }

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(enginesJsonPath))!;
        var pluginsDir = Path.GetFullPath(Path.Combine(baseDir, config.PluginsDirectory));

        foreach (var entry in config.Engines)
        {
            var label = string.IsNullOrWhiteSpace(entry.Id) ? entry.Assembly : entry.Id;
            if (!entry.Enabled)
            {
                diagnostics.Add(new(DiagnosticSeverity.Info, $"Engine '{label}' is disabled; skipped."));
                continue;
            }

            try
            {
                LoadEntry(entry, baseDir, pluginsDir, registry, diagnostics);
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
                diagnostics.Add(new(DiagnosticSeverity.Error, $"Engine '{label}' failed to load: {inner.Message}"));
            }
        }

        ReportUnlistedPlugins(config, pluginsDir, diagnostics);
        return new EngineLoadResult(registry, diagnostics);
    }

    private static void LoadEntry(EngineEntry entry, string baseDir, string pluginsDir, EngineRegistry registry, List<EngineDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(entry.Assembly))
            throw new InvalidOperationException("entry has no 'assembly'.");

        var assembly = ResolveAssembly(entry.Assembly, pluginsDir);
        var moduleTypes = assembly.GetTypes()
            .Where(t => typeof(IEngineModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .Where(t => entry.Module is null || t.FullName == entry.Module)
            .ToList();

        if (moduleTypes.Count == 0)
            throw new InvalidOperationException($"no IEngineModule found in '{entry.Assembly}'" + (entry.Module is null ? "." : $" matching '{entry.Module}'."));

        var options = new EngineOptions(entry.Options, baseDir);
        foreach (var type in moduleTypes)
            Instantiate(type, options).Register(registry);

        var registered = registry.FindSource(entry.Id) is not null || registry.FindTarget(entry.Id) is not null;
        diagnostics.Add(registered
            ? new(DiagnosticSeverity.Info, $"Loaded engine '{entry.Id}' from {assembly.GetName().Name}.")
            : new(DiagnosticSeverity.Warning, $"Assembly '{entry.Assembly}' loaded but did not register an engine with id '{entry.Id}'."));
    }

    private static IEngineModule Instantiate(Type type, EngineOptions options)
    {
        var withOptions = type.GetConstructor(new[] { typeof(EngineOptions) });
        if (withOptions is not null) return (IEngineModule)withOptions.Invoke(new object[] { options });

        var parameterless = type.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException($"{type.FullName} needs a public parameterless or (EngineOptions) constructor.");
        return (IEngineModule)parameterless.Invoke(null);
    }

    private static Assembly ResolveAssembly(string name, string pluginsDir)
    {
        // Built-in: part of the host's app directory (the composition root references it).
        var builtIn = Path.Combine(AppContext.BaseDirectory, name + ".dll");
        if (File.Exists(builtIn))
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(builtIn);

        // Plugin: plugins/<name>.dll or plugins/<name>/<name>.dll
        var candidates = new[]
        {
            Path.Combine(pluginsDir, name + ".dll"),
            Path.Combine(pluginsDir, name, name + ".dll")
        };
        var path = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"assembly '{name}' not found in the app directory or '{pluginsDir}'.");

        return new PluginLoadContext(path).LoadFromAssemblyPath(path);
    }

    /// <summary>engines.json is the allow-list; DLLs sitting in plugins/ that are not listed never execute, but we say so.</summary>
    private static void ReportUnlistedPlugins(EnginesConfig config, string pluginsDir, List<EngineDiagnostic> diagnostics)
    {
        if (!Directory.Exists(pluginsDir)) return;

        var listed = config.Engines.Select(e => e.Assembly).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = Directory.EnumerateFiles(pluginsDir, "*.dll", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateDirectories(pluginsDir).SelectMany(d =>
                Directory.EnumerateFiles(d, Path.GetFileName(d) + ".dll", SearchOption.TopDirectoryOnly)))
            .Select(p => Path.GetFileNameWithoutExtension(p));

        foreach (var name in found.Where(n => !listed.Contains(n)))
            diagnostics.Add(new(DiagnosticSeverity.Warning, $"Plugin assembly '{name}' is in plugins/ but not listed in engines.json; not loaded."));
    }
}
