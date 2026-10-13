namespace Migrator.Target.React.Generation;

/// <summary>
/// Files of the WinForms-style app shell (`migrate --appshell`): a bordered host window, a left pane of hyperlinks, and a right container that
/// loads the selected unit inside a form-style frame. Same manifest and visual-only pages as the viewer; only the shell differs. Generated output.
/// </summary>
internal static class AppShellTemplates
{
    /// <summary>
    /// Colours live in this one variable block (classic system-style defaults, mapped to the UI library tokens when they exist);
    /// nothing below it hard-codes a colour, so a theme can restyle the whole shell.
    /// </summary>
    public const string Css = """
        .as-root {
          --as-desktop: var(--wf-color-desktop, #6d7f99);
          --as-face: var(--wf-color-surface, #f0f0f0);
          --as-window: var(--wf-color-bg, #ffffff);
          --as-text: var(--wf-color-text, #1f1f1f);
          --as-muted: #6a6a6a;
          --as-link: var(--wf-color-link, #0066cc);
          --as-link-active: #c42b1c;
          --as-selected: var(--wf-color-selected, #cce8ff);
          --as-hover: var(--wf-color-hover, #e5f1fb);
          --as-border: var(--wf-color-border, #adadad);
          --as-border-dark: #6b6b6b;
          --as-highlight: #ffffff;
          --as-shadow: rgba(0, 0, 0, .28);
          --as-caption-bg: #2b579a;
          --as-caption-fg: #ffffff;
          --as-form-title-bg: #f7f7f7;
          --as-form-title-fg: #1f1f1f;
          --as-warn-bg: #fff4ce;
          --as-warn-border: #e5c85a;
          --as-error: #b00020;
          --as-font: var(--wf-font-family, "Segoe UI", Tahoma, Arial, sans-serif);
          --as-nav-width: 280px;

          box-sizing: border-box; height: 100%; padding: 12px; background: var(--as-desktop);
          font: 12px/1.4 var(--as-font); color: var(--as-text);
        }
        .as-root *, .as-root *::before, .as-root *::after { box-sizing: border-box; }

        .as-window {
          display: grid; grid-template-rows: auto minmax(0, 1fr) auto; height: 100%; min-height: 0;
          background: var(--as-face); border: 1px solid var(--as-border-dark);
          box-shadow: inset 1px 1px 0 var(--as-highlight), 0 6px 22px var(--as-shadow);
        }
        .as-caption { display: flex; align-items: center; gap: 8px; padding: 6px 10px; background: var(--as-caption-bg); color: var(--as-caption-fg); font-weight: 600; user-select: none; }
        .as-caption-icon { width: 14px; height: 14px; border: 1px solid var(--as-caption-fg); border-radius: 2px; opacity: .9; }
        .as-caption small { font-weight: 400; opacity: .8; margin-left: auto; }

        .as-body { display: flex; min-height: 0; padding: 6px; gap: 0; }

        .as-nav {
          flex: 0 0 var(--as-nav-width); display: flex; flex-direction: column; min-height: 0; min-width: 0;
          background: var(--as-window); border: 1px solid var(--as-border); box-shadow: inset 1px 1px 2px var(--as-shadow);
        }
        .as-nav-head { padding: 6px 8px; font-weight: 600; background: var(--as-face); border-bottom: 1px solid var(--as-border); }
        .as-nav-filter { padding: 6px 8px; border-bottom: 1px solid var(--as-border); }
        .as-nav-filter input { width: 100%; padding: 3px 6px; border: 1px solid var(--as-border); background: var(--as-window); color: inherit; font: inherit; }
        .as-nav-list { flex: 1 1 auto; overflow: auto; padding: 6px 4px 10px; }
        .as-kind { margin: 10px 4px 2px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .05em; color: var(--as-muted); }
        .as-group { display: flex; align-items: center; gap: 4px; width: 100%; padding: 2px 4px; border: 0; background: none; font: inherit; font-weight: 600; color: inherit; text-align: left; cursor: pointer; }
        .as-group:hover { background: var(--as-hover); }
        .as-count { margin-left: auto; color: var(--as-muted); font-weight: 400; }
        .as-links { list-style: none; margin: 0 0 4px; padding: 0 0 0 14px; }
        .as-link {
          display: block; padding: 2px 6px 2px 8px; border-left: 3px solid transparent;
          color: var(--as-link); text-decoration: none; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
        }
        .as-link:hover { text-decoration: underline; background: var(--as-hover); }
        .as-link:active { color: var(--as-link-active); }
        .as-link:focus-visible { outline: 1px dotted var(--as-text); outline-offset: -1px; }
        .as-link[aria-current="page"] { font-weight: 700; background: var(--as-selected); border-left-color: var(--as-link); }
        .as-link.has-error { color: var(--as-error); }
        .as-note { padding: 14px 10px; color: var(--as-muted); }

        .as-splitter { flex: 0 0 8px; cursor: col-resize; touch-action: none; position: relative; }
        .as-splitter::after { content: ""; position: absolute; top: 0; bottom: 0; left: 3px; width: 2px; background: var(--as-border); }
        .as-splitter:hover::after, .as-splitter:focus-visible::after { background: var(--as-link); }
        .as-splitter:focus-visible { outline: none; }

        .as-host { flex: 1 1 auto; min-width: 0; min-height: 0; display: flex; flex-direction: column; }
        .as-toolbar { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; padding: 4px 2px 6px; }
        .as-toolbar button, .as-toolbar select {
          padding: 2px 10px; font: inherit; color: inherit; background: var(--as-face);
          border: 1px solid var(--as-border); box-shadow: inset 1px 1px 0 var(--as-highlight);
        }
        .as-toolbar button:hover { background: var(--as-hover); }
        .as-toolbar .as-spacer { flex: 1 1 auto; }
        .as-chip { padding: 0 8px; border: 1px solid var(--as-border); background: var(--as-window); }
        .as-warn { margin: 0 0 6px; padding: 4px 8px; background: var(--as-warn-bg); border: 1px solid var(--as-warn-border); }

        .as-stage { flex: 1 1 auto; min-height: 0; overflow: auto; padding: 14px; display: flex; background: var(--as-face); border: 1px solid var(--as-border); box-shadow: inset 1px 1px 2px var(--as-shadow); }
        .as-form {
          flex: 1 1 auto; min-width: 0; min-height: 0; display: flex; flex-direction: column; align-self: stretch;
          background: var(--as-window); border: 1px solid var(--as-border-dark); box-shadow: 0 3px 10px var(--as-shadow);
        }
        .as-form.is-sized { flex: 0 0 auto; align-self: flex-start; }
        .as-form-title { display: flex; align-items: center; gap: 8px; padding: 5px 10px; background: var(--as-form-title-bg); color: var(--as-form-title-fg); border-bottom: 1px solid var(--as-border); font-weight: 600; user-select: none; }
        .as-form-title small { margin-left: auto; font-weight: 400; color: var(--as-muted); }
        .as-form-client { flex: 1 1 auto; min-height: 0; overflow: auto; display: flex; flex-direction: column; background: var(--as-face); }
        .as-form-client > * { flex: 1 1 auto; }
        .as-welcome { margin: auto; max-width: 420px; text-align: center; color: var(--as-muted); }
        .as-loading { padding: 24px; color: var(--as-muted); }
        .as-error { margin: 0; padding: 12px; color: var(--as-error); white-space: pre-wrap; }

        .as-status { display: flex; gap: 0; border-top: 1px solid var(--as-border); background: var(--as-face); }
        .as-status span { padding: 3px 10px; border-right: 1px solid var(--as-border); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .as-status span:last-child { border-right: 0; flex: 1 1 auto; }
        """;

    public const string Main = """
        import { Component, StrictMode, Suspense, lazy, useEffect, useMemo, useRef, useState, type ComponentType, type PointerEvent as ReactPointerEvent, type ReactNode } from 'react';
        import { createRoot } from 'react-dom/client';
        import { WfDialogProvider } from '@wfm/ui-react';
        import '@wfm/ui-react/styles.css';
        import './app.css';
        import './appshell.css';
        import { shell, units, type ViewerUnit } from './manifest';

        // Each unit is its own lazily loaded, visual-only module: choosing a link loads only that page into the right-hand container.
        const loaders = import.meta.glob('./pages/*.tsx') as Record<string, () => Promise<Record<string, unknown>>>;

        // Created once at module level (a lazy component made during render would reload on every render).
        const pages = new Map<string, ComponentType>(
          units.filter((u) => !u.error).map((u) => [u.id, lazy(async () => {
            const load = loaders[`./pages/${u.file}.tsx`];
            if (!load) throw new Error(`The page file of ${u.id} was not generated.`);
            return { default: (await load())[u.component] as ComponentType };
          })]),
        );

        // An error in one unit is shown for that unit only: the boundary is keyed by the selection, so choosing another link resets it.
        class Boundary extends Component<{ children: ReactNode }, { error: Error | null }> {
          state = { error: null as Error | null };
          static getDerivedStateFromError(error: Error) { return { error }; }
          render() { return this.state.error ? <pre className="as-error" role="alert">{String(this.state.error.stack ?? this.state.error)}</pre> : this.props.children; }
        }

        const readHash = (): string | null => {
          const id = decodeURIComponent(window.location.hash.replace(/^#\/?/, ''));
          return id.length > 0 ? id : null;
        };
        const hrefFor = (id: string) => `#/${encodeURIComponent(id)}`;
        const folderLabel = (group: string) => group.split('/').slice(-2).join('/');
        const MIN_NAV = 180;
        const MAX_NAV = 560;

        function step(list: ViewerUnit[], current: string | null, delta: number) {
          if (list.length === 0) return;
          const index = list.findIndex((u) => u.id === current);
          const start = index < 0 ? (delta > 0 ? -1 : list.length) : index;
          window.location.hash = hrefFor(list[Math.min(Math.max(start + delta, 0), list.length - 1)].id).slice(1);
        }

        function App() {
          const [selectedId, setSelectedId] = useState<string | null>(readHash);
          const [query, setQuery] = useState('');
          const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());
          const [framed, setFramed] = useState(false);
          const [navWidth, setNavWidth] = useState(280);
          const drag = useRef<{ x: number; width: number } | null>(null);

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
              const target = e.target as HTMLElement;
              if (target.tagName === 'INPUT' || target.tagName === 'SELECT' || (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') || target.getAttribute('role') === 'separator') return;
              e.preventDefault();
              step(visible, selectedId, e.key === 'ArrowDown' ? 1 : -1);
            };
            window.addEventListener('keydown', onKey);
            return () => window.removeEventListener('keydown', onKey);
          }, [visible, selectedId]);

          const tree = useMemo(() => ['form', 'control'].map((kind) => {
            const groups = new Map<string, ViewerUnit[]>();
            for (const u of visible.filter((x) => x.kind === kind)) groups.set(u.group, [...(groups.get(u.group) ?? []), u]);
            return { kind, label: kind === 'form' ? 'Forms' : 'User controls', count: [...groups.values()].reduce((n, items) => n + items.length, 0), groups: [...groups.entries()].sort(([a], [b]) => a.localeCompare(b)) };
          }), [visible]);

          const toggle = (key: string) => setCollapsed((current) => {
            const next = new Set(current);
            if (!next.delete(key)) next.add(key);
            return next;
          });

          const onSplitDown = (e: ReactPointerEvent<HTMLDivElement>) => {
            drag.current = { x: e.clientX, width: navWidth };
            e.currentTarget.setPointerCapture(e.pointerId);
          };
          const onSplitMove = (e: ReactPointerEvent<HTMLDivElement>) => {
            if (drag.current) setNavWidth(Math.min(MAX_NAV, Math.max(MIN_NAV, drag.current.width + e.clientX - drag.current.x)));
          };

          const selected = units.find((u) => u.id === selectedId) ?? null;
          const Page = selected ? pages.get(selected.id) ?? null : null;
          const warnings = selected ? [...selected.fallbacks.map((f) => `fallback: ${f}`), ...selected.unsupported.map((u) => `unsupported: ${u}`)] : [];
          const sized = framed && !!selected?.designWidth;

          return (
            <div className="as-root" style={{ ['--as-nav-width' as string]: `${navWidth}px` }}>
              <div className="as-window">
                <div className="as-caption">
                  <span className="as-caption-icon" aria-hidden="true" />
                  <span>{shell.title}</span>
                  <small>Application shell</small>
                </div>

                <div className="as-body">
                  <nav className="as-nav" aria-label="Forms and user controls">
                    <div className="as-nav-head">Navigation</div>
                    <div className="as-nav-filter">
                      <input type="search" placeholder="Filter" value={query} onChange={(e) => setQuery(e.target.value)} aria-label="Filter links" />
                    </div>
                    <div className="as-nav-list">
                      {visible.length === 0 && <p className="as-note">{units.length === 0 ? 'No forms or user controls were generated.' : 'Nothing matches the filter.'}</p>}
                      {tree.map((section) => section.count > 0 && (
                        <section key={section.kind}>
                          <div className="as-kind">{section.label} ({section.count})</div>
                          {section.groups.map(([group, items]) => {
                            const key = `${section.kind}:${group}`;
                            const open = query.length > 0 || !collapsed.has(key);
                            return (
                              <div key={key}>
                                {group && <button type="button" className="as-group" onClick={() => toggle(key)} title={group} aria-expanded={open}>{open ? '▾' : '▸'} {folderLabel(group)}<span className="as-count">{items.length}</span></button>}
                                {open && (
                                  <ul className="as-links">
                                    {items.map((u) => (
                                      <li key={u.id}>
                                        <a className={`as-link${u.error ? ' has-error' : ''}`} href={hrefFor(u.id)} aria-current={u.id === selectedId ? 'page' : undefined} title={u.source}>{u.id}</a>
                                      </li>
                                    ))}
                                  </ul>
                                )}
                              </div>
                            );
                          })}
                        </section>
                      ))}
                    </div>
                  </nav>

                  <div
                    className="as-splitter" role="separator" aria-orientation="vertical" aria-label="Resize navigation" aria-valuemin={MIN_NAV} aria-valuemax={MAX_NAV} aria-valuenow={navWidth} tabIndex={0}
                    onPointerDown={onSplitDown} onPointerMove={onSplitMove} onPointerUp={() => { drag.current = null; }}
                    onKeyDown={(e) => {
                      if (e.key === 'ArrowLeft') setNavWidth((w) => Math.max(MIN_NAV, w - 16));
                      if (e.key === 'ArrowRight') setNavWidth((w) => Math.min(MAX_NAV, w + 16));
                    }}
                  />

                  <main className="as-host">
                    <div className="as-toolbar">
                      <button type="button" onClick={() => step(visible, selectedId, -1)}>◀ Previous</button>
                      <button type="button" onClick={() => step(visible, selectedId, 1)}>Next ▶</button>
                      <span className="as-spacer" />
                      {selected && <span className="as-chip">{selected.kind === 'form' ? 'Form' : 'User control'}</span>}
                      <label><input type="checkbox" checked={framed} onChange={(e) => setFramed(e.target.checked)} disabled={!selected?.designWidth} /> Design size</label>
                    </div>
                    {warnings.length > 0 && <div className="as-warn">{warnings.join(' · ')}</div>}
                    <div className="as-stage">
                      {selected ? (
                        <div className={`as-form${sized ? ' is-sized' : ''}`} style={sized ? { width: selected.designWidth, minHeight: selected.designHeight } : undefined}>
                          <div className="as-form-title"><span>{selected.title}</span><small>{selected.source}</small></div>
                          <div className="as-form-client">
                            {selected.error || !Page ? (
                              <pre className="as-error" role="alert">{selected.error ?? 'This unit has no page.'}</pre>
                            ) : (
                              <Suspense fallback={<p className="as-loading">Loading {selected.id}…</p>}>
                                <Boundary key={selected.id}>
                                  {/* eslint-disable-next-line react-hooks/static-components -- the lazy components come from the module-level map; none is created during render */}
                                  <WfDialogProvider><Page /></WfDialogProvider>
                                </Boundary>
                              </Suspense>
                            )}
                          </div>
                        </div>
                      ) : (
                        <div className="as-form">
                          <div className="as-form-title"><span>{shell.title}</span></div>
                          <div className="as-form-client"><p className="as-welcome">Choose a form or user control from the navigation on the left. It opens here in its own frame. Arrow keys step through the links.</p></div>
                        </div>
                      )}
                    </div>
                  </main>
                </div>

                <div className="as-status" role="status">
                  <span>{units.length} units</span>
                  <span>{selected ? (selected.kind === 'form' ? 'Form' : 'User control') : 'Ready'}</span>
                  <span>{selected ? `${selected.id} — ${selected.controls} controls` : 'No unit selected'}</span>
                </div>
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
