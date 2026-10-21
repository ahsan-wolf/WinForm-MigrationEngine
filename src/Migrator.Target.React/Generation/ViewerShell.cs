using System.Text.Json;
using System.Text.Json.Serialization;
using Migrator.Target.Core.Ir;

namespace Migrator.Target.React.Generation;

/// <summary>One form or user control in the viewer: where its page is, how to list it, and what to warn about.</summary>
public sealed record ViewerUnit(
    string Id, string Component, string File, string Kind, string Title, string Group, string Source, int Controls,
    List<string> Fallbacks, List<string> Unsupported, int? DesignWidth, int? DesignHeight, string? Error);

/// <summary>
/// The viewer app around visual-only pages (`migrate --preview`): a manifest of every unit plus a shell that lists them in a sidebar and
/// loads only the selected one. Generated output; nothing here is part of the repository as an app. Knows no application names.
/// </summary>
public static class ViewerShell
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <param name="title">Shown in the shell's caption (the project folder name).</param>
    /// <param name="appShell">The WinForms-style app shell instead of the plain viewer; manifest and pages are identical.</param>
    public static void Write(string appDir, string appName, string uiLibraryDir, IReadOnlyList<ViewerUnit> units, string title, bool appShell, List<string> files)
    {
        AppWriter.Write(appDir, appName, entryComponent: null, uiLibraryDir, files);
        AppWriter.Put(appDir, "src/manifest.ts", Manifest(units, title), files);
        if (appShell)
        {
            AppWriter.Put(appDir, "src/appshell.css", AppShellTemplates.Css, files);
            AppWriter.Put(appDir, "src/main.tsx", AppShellTemplates.Main, files);
        }
        else
        {
            AppWriter.Put(appDir, "src/viewer.css", ViewerCss, files);
            AppWriter.Put(appDir, "src/main.tsx", ViewerMain, files);
        }
    }

    private static string Manifest(IReadOnlyList<ViewerUnit> units, string title) =>
        "// Generated. Every form and user control of the project, for the viewer sidebar / app shell navigation.\n" +
        "export const shell = { title: " + JsonSerializer.Serialize(title) + " };\n\n" +
        "export interface ViewerUnit {\n" +
        "  id: string;\n  component: string;\n  file: string;\n  kind: 'form' | 'control';\n  title: string;\n  group: string;\n  source: string;\n" +
        "  controls: number;\n  fallbacks: string[];\n  unsupported: string[];\n  designWidth?: number;\n  designHeight?: number;\n  error?: string;\n}\n\n" +
        "export const units: ViewerUnit[] = " + JsonSerializer.Serialize(units, Json) + ";\n";

    private const string ViewerCss = """
        .v-root { display: grid; grid-template-rows: auto minmax(0, 1fr); height: 100%; font-family: var(--wf-font-family, system-ui, sans-serif); color: var(--wf-color-text, #1f1f1f); }
        .v-header { display: flex; align-items: center; gap: 12px; padding: 8px 12px; border-bottom: 1px solid var(--wf-color-border, #c8c8c8); background: var(--wf-color-surface, #f5f5f5); }
        .v-header h1 { margin: 0; font-size: 1rem; }
        .v-header input[type=search] { margin-left: auto; min-width: 240px; padding: 4px 8px; }
        .v-body { display: grid; grid-template-columns: 320px minmax(0, 1fr); min-height: 0; }
        .v-nav { overflow: auto; border-right: 1px solid var(--wf-color-border, #c8c8c8); padding: 8px; }
        .v-nav h2 { font-size: .8rem; text-transform: uppercase; letter-spacing: .04em; margin: 12px 4px 4px; }
        .v-nav ul { list-style: none; margin: 0 0 4px; padding: 0 0 0 8px; }
        .v-nav small { opacity: .65; margin-left: 6px; }
        .v-group, .v-unit { display: block; width: 100%; text-align: left; border: 0; background: none; padding: 3px 6px; cursor: pointer; font: inherit; color: inherit; border-radius: 3px; }
        .v-group { font-weight: 600; opacity: .85; }
        .v-unit:hover, .v-group:hover { background: var(--wf-color-hover, #e9e9e9); }
        .v-unit.is-selected { background: var(--wf-color-selected, #cfe3ff); }
        .v-unit.has-error { color: #b00020; }
        .v-main { display: flex; flex-direction: column; min-width: 0; min-height: 0; }
        .v-bar { display: flex; align-items: center; gap: 12px; padding: 8px 12px; border-bottom: 1px solid var(--wf-color-border, #c8c8c8); flex-wrap: wrap; }
        .v-chip { padding: 1px 8px; border-radius: 10px; background: var(--wf-color-selected, #cfe3ff); font-size: .75rem; }
        .v-source { opacity: .65; font-size: .8rem; }
        .v-spacer { flex: 1 1 auto; }
        .v-notes { padding: 6px 12px; font-size: .8rem; background: #fff8e1; border-bottom: 1px solid #ecd9a0; }
        .v-stage { flex: 1 1 auto; min-height: 0; overflow: auto; padding: 16px; display: flex; }
        .v-stage.bg-light { background: #ffffff; } .v-stage.bg-grey { background: #e4e4e4; } .v-stage.bg-dark { background: #1e1e1e; }
        .v-frame { flex: 1 1 auto; min-width: 0; min-height: 0; display: flex; flex-direction: column; background: #fff; box-shadow: 0 0 0 1px var(--wf-color-border, #c8c8c8); }
        .v-frame.is-sized { flex: 0 0 auto; }
        .v-frame > * { flex: 1 1 auto; }
        .v-loading, .v-empty { padding: 24px; opacity: .7; }
        .v-error { margin: 0; padding: 12px; color: #b00020; white-space: pre-wrap; }
        """;

    private const string ViewerMain = """
        import { Component, StrictMode, Suspense, lazy, useEffect, useMemo, useState, type ComponentType, type ReactNode } from 'react';
        import { createRoot } from 'react-dom/client';
        import { WfDialogProvider } from '@wfm/ui-react';
        import '@wfm/ui-react/styles.css';
        import './app.css';
        import './viewer.css';
        import { units, type ViewerUnit } from './manifest';

        // Every unit is its own lazily loaded module: opening one loads only that page. Pages are visual-only (no handlers, no data, no props).
        const loaders = import.meta.glob('./pages/*.tsx') as Record<string, () => Promise<Record<string, unknown>>>;
        // Created once at module level (a lazy component made during render would reload on every render).
        const pages = new Map<string, ComponentType>(
          units.filter((u) => !u.error).map((u) => [u.id, lazy(async () => {
            const load = loaders[`./pages/${u.file}.tsx`];
            if (!load) throw new Error(`The page file of ${u.id} was not generated.`);
            return { default: (await load())[u.component] as ComponentType };
          })]),
        );

        // An error in one unit is shown for that unit only: the boundary is keyed by the selection, so choosing another unit resets it.
        class Boundary extends Component<{ children: ReactNode }, { error: Error | null }> {
          state = { error: null as Error | null };
          static getDerivedStateFromError(error: Error) { return { error }; }
          render() { return this.state.error ? <pre className="v-error" role="alert">{String(this.state.error.stack ?? this.state.error)}</pre> : this.props.children; }
        }

        type Background = 'light' | 'grey' | 'dark';

        function readHash(): string | null {
          const id = decodeURIComponent(window.location.hash.replace(/^#\/?/, ''));
          return id.length > 0 ? id : null;
        }

        function selectUnit(id: string | null) {
          window.location.hash = id ? `/${encodeURIComponent(id)}` : '';
        }

        function stepIn(list: ViewerUnit[], current: string | null, delta: number) {
          if (list.length === 0) return;
          const index = list.findIndex((u) => u.id === current);
          const start = index < 0 ? (delta > 0 ? -1 : list.length) : index;
          selectUnit(list[Math.min(Math.max(start + delta, 0), list.length - 1)].id);
        }

        const folderLabel = (group: string) => group.split('/').slice(-2).join('/');

        function App() {
          const [selectedId, setSelectedId] = useState<string | null>(readHash);
          const [query, setQuery] = useState('');
          const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());
          const [framed, setFramed] = useState(false);
          const [background, setBackground] = useState<Background>('light');

          useEffect(() => {
            const onHash = () => setSelectedId(readHash());
            window.addEventListener('hashchange', onHash);
            return () => window.removeEventListener('hashchange', onHash);
          }, []);

          const visible = useMemo(() => {
            const q = query.trim().toLowerCase();
            return q ? units.filter((u) => `${u.id} ${u.title} ${u.group}`.toLowerCase().includes(q)) : units;
          }, [query]);

          useEffect(() => {
            const onKey = (e: KeyboardEvent) => {
              if ((e.target as HTMLElement).tagName === 'INPUT' || (e.key !== 'ArrowDown' && e.key !== 'ArrowUp')) return;
              e.preventDefault();
              stepIn(visible, selectedId, e.key === 'ArrowDown' ? 1 : -1);
            };
            window.addEventListener('keydown', onKey);
            return () => window.removeEventListener('keydown', onKey);
          }, [visible, selectedId]);

          const tree = useMemo(() => {
            const sections = (kind: ViewerUnit['kind'], label: string) => {
              const groups = new Map<string, ViewerUnit[]>();
              for (const u of visible.filter((x) => x.kind === kind)) groups.set(u.group, [...(groups.get(u.group) ?? []), u]);
              return { kind, label, count: [...groups.values()].reduce((n, items) => n + items.length, 0), groups: [...groups.entries()].sort(([a], [b]) => a.localeCompare(b)) };
            };
            return [sections('form', 'Forms'), sections('control', 'User controls')];
          }, [visible]);

          const toggle = (key: string) => setCollapsed((current) => {
            const next = new Set(current);
            if (!next.delete(key)) next.add(key);
            return next;
          });

          const selected = units.find((u) => u.id === selectedId) ?? null;
          const Page = selected ? pages.get(selected.id) ?? null : null;
          const warnings = selected ? [...selected.fallbacks.map((f) => `fallback: ${f}`), ...selected.unsupported.map((u) => `unsupported: ${u}`)] : [];

          return (
            <div className="v-root">
              <header className="v-header">
                <h1>Component viewer</h1>
                <span>{units.length} units</span>
                <input type="search" placeholder="Filter forms and controls" value={query} onChange={(e) => setQuery(e.target.value)} aria-label="Filter units" />
              </header>
              <div className="v-body">
                <nav className="v-nav" aria-label="Units">
                  {visible.length === 0 && <p className="v-empty">{units.length === 0 ? 'No forms or user controls were generated.' : 'Nothing matches the filter.'}</p>}
                  {tree.map((section) => section.count > 0 && (
                    <section key={section.kind}>
                      <h2>{section.label}<small>{section.count}</small></h2>
                      {section.groups.map(([group, items]) => {
                        const key = `${section.kind}:${group}`;
                        const open = query.length > 0 || !collapsed.has(key);
                        return (
                          <div key={key}>
                            {group && <button type="button" className="v-group" onClick={() => toggle(key)} title={group}>{open ? '▾' : '▸'} {folderLabel(group)}<small>{items.length}</small></button>}
                            {open && (
                              <ul>
                                {items.map((u) => (
                                  <li key={u.id}>
                                    <button type="button" className={`v-unit${u.id === selectedId ? ' is-selected' : ''}${u.error ? ' has-error' : ''}`} onClick={() => selectUnit(u.id)} title={u.source}>
                                      {u.id}{u.title !== u.id && <small>{u.title}</small>}
                                    </button>
                                  </li>
                                ))}
                              </ul>
                            )}
                          </div>
                        );
                      })}
                    </section>
                  ))}
                </nav>
                <main className="v-main">
                  {selected ? (
                    <>
                      <div className="v-bar">
                        <strong>{selected.title}</strong>
                        <span className="v-chip">{selected.kind === 'form' ? 'form' : 'user control'}</span>
                        <span className="v-source">{selected.source} · {selected.controls} controls</span>
                        <span className="v-spacer" />
                        <label><input type="checkbox" checked={framed} onChange={(e) => setFramed(e.target.checked)} /> design size</label>
                        <select value={background} onChange={(e) => setBackground(e.target.value as Background)} aria-label="Background">
                          <option value="light">light</option><option value="grey">grey</option><option value="dark">dark</option>
                        </select>
                        <button type="button" onClick={() => stepIn(visible, selectedId, -1)}>Previous</button>
                        <button type="button" onClick={() => stepIn(visible, selectedId, 1)}>Next</button>
                      </div>
                      {warnings.length > 0 && <div className="v-notes">{warnings.join(' · ')}</div>}
                      <div className={`v-stage bg-${background}`}>
                        {selected.error || !Page ? (
                          <pre className="v-error" role="alert">{selected.error ?? 'This unit has no page.'}</pre>
                        ) : (
                          <div className={`v-frame${framed && selected.designWidth ? ' is-sized' : ''}`} style={framed && selected.designWidth ? { width: selected.designWidth, minHeight: selected.designHeight } : undefined}>
                            <Suspense fallback={<p className="v-loading">Loading {selected.id}…</p>}>
                              <Boundary key={selected.id}>
                                {/* eslint-disable-next-line react-hooks/static-components -- the lazy components come from the module-level map; none is created during render */}
                                <WfDialogProvider><Page /></WfDialogProvider>
                              </Boundary>
                            </Suspense>
                          </div>
                        )}
                      </div>
                    </>
                  ) : (
                    <p className="v-empty">Choose a form or user control on the left. Arrow keys step through the list.</p>
                  )}
                </main>
              </div>
            </div>
          );
        }

        createRoot(document.getElementById('root')!).render(
          <StrictMode>
            <App />
          </StrictMode>,
        );
        """;
}
