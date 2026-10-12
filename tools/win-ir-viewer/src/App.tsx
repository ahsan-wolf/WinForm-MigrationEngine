import { useEffect, useMemo, useState } from 'react';
import { fileSource, fromDrop, fromFileList } from './browser';
import {
  allUnits, countControls, loadProject, loadUnit,
  type LoadedProject, type LoadedUnit, type UnitRef,
} from './ir';
import { LayoutView, MembersView, NavigationView, PropertiesView, TreeView, WorkflowsView } from './views';

const TABS = ['Tree', 'Layout', 'Members', 'Workflows', 'Navigation', 'Properties'] as const;
type Tab = (typeof TABS)[number];

export default function App() {
  const [project, setProject] = useState<LoadedProject | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<UnitRef | null>(null);
  const [unit, setUnit] = useState<LoadedUnit | null>(null);
  const [tab, setTab] = useState<Tab>('Tree');
  const [dragging, setDragging] = useState(false);

  async function open(files: Map<string, File>) {
    setError(null);
    try {
      const loaded = await loadProject(fileSource(files));
      setProject(loaded);
      const start = allUnits(loaded.project).find(u => u.Name === loaded.project.Source.StartupForm) ?? allUnits(loaded.project)[0] ?? null;
      setSelected(start);
    } catch (e) {
      setProject(null);
      setSelected(null);
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  useEffect(() => {
    if (!project || !selected) return;
    let cancelled = false;
    loadUnit(project, selected)
      .then(u => { if (!cancelled) { setUnit(u); setError(null); } })
      .catch((e: unknown) => { if (!cancelled) { setUnit(null); setError(e instanceof Error ? e.message : String(e)); } });
    return () => { cancelled = true; };
  }, [project, selected]);

  const names = useMemo(() => new Set(project ? allUnits(project.project).map(u => u.Name) : []), [project]);
  const openByName = (name: string) => {
    const ref = project ? allUnits(project.project).find(u => u.Name === name) : undefined;
    if (ref) { setSelected(ref); setTab('Tree'); }
  };

  return (
    <div
      className={`app${dragging ? ' dragging' : ''}`}
      onDragOver={e => { e.preventDefault(); setDragging(true); }}
      onDragLeave={e => { if (e.currentTarget === e.target) setDragging(false); }}
      onDrop={e => { e.preventDefault(); setDragging(false); void fromDrop(e.dataTransfer).then(open); }}
    >
      <header>
        <h1>WinForms IR Viewer</h1>
        <label className="pick">
          Open IR folder…
          <input type="file" multiple ref={el => el?.setAttribute('webkitdirectory', '')}
                 onChange={e => { if (e.target.files) void open(fromFileList(e.target.files)); }} />
        </label>
        {project && <span className="muted">{project.project.Source.Technology} · {project.project.Source.FrameworkVersion ?? '?'} · startup: {project.project.Source.StartupForm ?? '?'}</span>}
      </header>

      {error && <div className="error">{error}</div>}

      {!project ? (
        <p className="empty big">Choose or drop the <code>ir</code> folder written by <code>migrator ir</code> (the one containing <code>project.ir.json</code>).</p>
      ) : (
        <main>
          <aside>
            <h2>Forms ({project.project.Forms.length})</h2>
            {project.project.Forms.map(u => <UnitButton key={u.Name} u={u} selected={selected} onPick={setSelected} startup={project.project.Source.StartupForm} />)}
            <h2>User controls ({project.project.UserControls.length})</h2>
            {project.project.UserControls.map(u => <UnitButton key={u.Name} u={u} selected={selected} onPick={setSelected} startup={null} />)}
            {project.project.Failures.length > 0 && (
              <><h2 className="bad">Failures ({project.project.Failures.length})</h2>
                {project.project.Failures.map(f => <div key={f.Name} className="fail"><b>{f.Name}</b>: {f.Error}</div>)}</>
            )}
            {project.project.Shared.Unsupported.length > 0 && (
              <details><summary><h2 className="inline">Unsupported ({project.project.Shared.Unsupported.length})</h2></summary>
                {project.project.Shared.Unsupported.map((u, i) => <div key={i} className="fail">{u}</div>)}</details>
            )}
          </aside>
          <section>
            {unit && selected ? (
              <>
                <div className="unit-head">
                  <b>{selected.Name}</b> <span className="muted">{selected.Kind} · {countControls(unit.page.RootControl)} controls · {unit.page.Members.length} members · {unit.page.Workflows.length} workflows · {unit.page.Navigations.length} navigation</span>
                </div>
                <nav className="tabs">{TABS.map(t => <button key={t} className={t === tab ? 'on' : ''} onClick={() => setTab(t)}>{t}</button>)}</nav>
                {tab === 'Tree' && <TreeView unit={unit} />}
                {tab === 'Layout' && <LayoutView unit={unit} />}
                {tab === 'Members' && <MembersView unit={unit} />}
                {tab === 'Workflows' && <WorkflowsView unit={unit} />}
                {tab === 'Navigation' && <NavigationView unit={unit} units={names} onOpen={openByName} />}
                {tab === 'Properties' && <PropertiesView unit={unit} />}
              </>
            ) : <p className="empty">Loading…</p>}
          </section>
        </main>
      )}
    </div>
  );
}

function UnitButton({ u, selected, onPick, startup }: { u: UnitRef; selected: UnitRef | null; onPick: (u: UnitRef) => void; startup: string | null }) {
  return (
    <button className={`unit${selected?.Name === u.Name ? ' on' : ''}`} onClick={() => onPick(u)}>
      {u.Name}{u.Name === startup && <span className="tag">startup</span>}
    </button>
  );
}
