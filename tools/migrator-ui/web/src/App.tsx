import { useCallback, useEffect, useRef, useState } from 'react';
import {
  WfButton, WfCheckBox, WfComboBox, WfGroupBox, WfLabel, WfProgress, WfRadioButton, WfTabPage, WfTabs, WfTextBox, useWfDialog,
} from '@wfm/ui-react';
import {
  followLog, getInfo, getReport, installApp, listRuns, startApp, startMigration, stopRun,
  type Info, type MigrateForm, type RunInfo,
} from './api';
import { FolderPicker } from './FolderPicker';

const STORAGE = 'migrator-ui.form.v1';
const DEFAULTS: MigrateForm = {
  source: '', out: '', mode: 'app', target: 'react-app', entry: '', depth: '1', allUnits: false, catalog: false, noExternalProjects: false,
  translate: 'rules', compileGate: true, contracts: '', contractScope: 'used', images: '', archetypes: '', api: false, dbProvider: 'sqlclient',
};
const MODES: { id: MigrateForm['mode']; text: string; hint: string }[] = [
  { id: 'ir', text: 'IR only', hint: 'The framework-neutral IR files (six per form + project.ir.json).' },
  { id: 'app', text: 'Working app', hint: 'IR + a generated app with translated handlers, routes and mock data.' },
  { id: 'appshell', text: 'App shell', hint: 'A WinForms-style host window with navigation on the left (visual-only pages, React).' },
  { id: 'viewer', text: 'Component viewer', hint: 'A visual-only page per form and user control with a sidebar (React).' },
];

function loadForm(): MigrateForm {
  try { return { ...DEFAULTS, ...(JSON.parse(localStorage.getItem(STORAGE) ?? '{}') as Partial<MigrateForm>) }; } catch { return DEFAULTS; }
}

/** The command line the form stands for, as the CLI would be called by hand. */
function preview(f: MigrateForm): string {
  const q = (s: string) => (/\s/.test(s) ? `"${s}"` : s);
  if (f.mode === 'ir') return `migrator ir --source ${q(f.source || '<source>')} --out ${q(f.out || '<out>')}`;
  const a = ['migrator migrate', `--source ${q(f.source || '<source>')}`, `--out ${q(f.out || '<out>')}`, `--target ${f.target}`];
  if (f.target === 'dotnet-api') return a.concat(f.dbProvider ? [`--db-provider ${f.dbProvider}`] : []).join(' ');
  if (f.api) a.push('--api');
  if (f.mode === 'appshell') a.push('--appshell');
  if (f.mode === 'viewer') a.push('--preview');
  if (f.entry) a.push(`--entry ${f.entry}`);
  if (f.depth) a.push(`--depth ${f.depth}`);
  if (f.allUnits) a.push('--units all');
  if (f.catalog) a.push('--catalog');
  if (f.noExternalProjects) a.push('--no-external-projects');
  if (f.mode === 'app') {
    if (f.translate) a.push(`--translate ${f.translate}`);
    if (f.compileGate) a.push('--compile-gate');
    for (const c of f.contracts.split(/\r?\n/).map(s => s.trim()).filter(Boolean)) a.push(`--contract ${q(c)}`);
    if (f.contractScope !== 'used') a.push(`--contract-scope ${f.contractScope}`);
  }
  if (f.images) a.push(`--images ${q(f.images)}`);
  if (f.archetypes) a.push(`--archetypes ${q(f.archetypes)}`);
  return a.join(' ');
}

export function App() {
  const dialog = useWfDialog();
  const [info, setInfo] = useState<Info | null>(null);
  const [form, setForm] = useState<MigrateForm>(loadForm);
  const [run, setRun] = useState<RunInfo | null>(null);
  const [runs, setRuns] = useState<RunInfo[]>([]);
  const [log, setLog] = useState<string[]>([]);
  const [report, setReport] = useState('');
  const [tab, setTab] = useState(0);
  const [error, setError] = useState('');
  const logEnd = useRef<HTMLPreElement>(null);
  const stopFollow = useRef<() => void>(() => undefined);

  const set = <K extends keyof MigrateForm>(key: K, value: MigrateForm[K]) => setForm(f => ({ ...f, [key]: value }));
  useEffect(() => { localStorage.setItem(STORAGE, JSON.stringify(form)); }, [form]);
  useEffect(() => { getInfo().then(setInfo).catch((e: Error) => setError(e.message)); }, []);
  useEffect(() => { logEnd.current?.scrollTo({ top: logEnd.current.scrollHeight }); }, [log]);

  // Poll the runs (dev servers become ready, finish or stop on their own).
  useEffect(() => {
    const tick = () => listRuns().then(setRuns).catch(() => undefined);
    tick();
    const timer = window.setInterval(tick, 2000);
    return () => window.clearInterval(timer);
  }, []);

  const target = info?.targets.find(t => t.id === form.target);
  const modeAllowed = (m: string) => !target || target.modes.includes(m);

  const pick = async (key: 'source' | 'out' | 'images', title: string) => {
    const chosen = await dialog.show<string>(done => <FolderPicker initial={form[key]} onDone={done} />, { title });
    if (chosen) set(key, chosen);
  };

  const follow = useCallback((r: RunInfo, then?: () => void) => {
    stopFollow.current();
    setLog([]);
    setRun(r);
    setTab(0);
    stopFollow.current = followLog(r.id, line => setLog(l => [...l, line]), () => {
      listRuns().then(all => { setRuns(all); setRun(all.find(x => x.id === r.id) ?? r); }).catch(() => undefined);
      then?.();
    });
  }, []);

  const go = async () => {
    setError('');
    setReport('');
    try {
      const r = await startMigration(form);
      follow(r, () => {
        if (form.mode === 'app') getReport(r.outputDirectory ?? form.out).then(setReport).catch(() => undefined);
      });
    } catch (e) { setError((e as Error).message); }
  };

  const act = async (fn: (dir: string) => Promise<RunInfo>, dir: string) => {
    setError('');
    try { follow(await fn(dir)); } catch (e) { setError((e as Error).message); }
  };

  const current = run ? runs.find(r => r.id === run.id) ?? run : null;
  const migrating = current?.kind === 'migrate' && !current.done;
  const lastMigration = [...runs].filter(r => r.kind === 'migrate').sort((a, b) => a.started.localeCompare(b.started)).at(-1);
  const appDir = lastMigration?.appDirectory ?? null;
  const dev = appDir ? runs.find(r => r.kind === 'dev' && !r.done && r.appDirectory?.toLowerCase() === appDir.toLowerCase()) : undefined;
  const isApp = form.mode === 'app';
  const angular = appDir?.toLowerCase().endsWith('angular-app') ?? false;
  const command = appDir ? `cd "${appDir}"\nnpm install\n${angular ? 'npx ng serve' : 'npm run dev'}` : '';
  const stop = (id: string) => stopRun(id).then(() => listRuns().then(setRuns));
  const copy = (text: string) => { void navigator.clipboard.writeText(text); };

  return (
    <div className="mu-shell">
      <header className="mu-header">
        <h1>WinForms Migrator</h1>
        <span className="mu-hint">{info ? (info.cliAvailable ? `Engine repo: ${info.repoRoot ?? 'not found'}` : 'migrator.dll is missing next to the host') : 'Connecting to the host…'}</span>
      </header>

      <div className="mu-main">
        <div className="mu-form">
          <WfGroupBox title="Source and output">
            <div className="mu-field">
              <WfLabel text="WinForms source" />
              <div className="mu-row"><WfTextBox value={form.source} onValueChange={v => set('source', v)} placeholder="folder with the .csproj / .sln" /><WfButton text="Browse…" onClick={() => void pick('source', 'WinForms source folder')} /></div>
              <WfLabel text="Output folder" />
              <div className="mu-row"><WfTextBox value={form.out} onValueChange={v => set('out', v)} placeholder="outside this repository" /><WfButton text="Browse…" onClick={() => void pick('out', 'Output folder')} /></div>
            </div>
          </WfGroupBox>

          <WfGroupBox title="What to generate">
            <div className="mu-modes">
              {MODES.map(m => (
                <WfRadioButton key={m.id} group="mode" text={m.text} tooltip={m.hint} checked={form.mode === m.id} disabled={!modeAllowed(m.id)} onCheckedChange={c => { if (c) set('mode', m.id); }} />
              ))}
            </div>
            <div className="mu-field" style={{ marginTop: 8 }}>
              <WfLabel text="Target" />
              <WfComboBox items={(info?.targets ?? [{ id: 'react-app' }]).map(t => t.id)} value={form.target} onSelectionChange={v => setForm(f => ({ ...f, target: v, mode: info?.targets.find(t => t.id === v)?.modes.includes(f.mode) === false ? 'app' : f.mode }))} />
            </div>
            <div className="mu-hint">{MODES.find(m => m.id === form.mode)?.hint}</div>
          </WfGroupBox>

          <WfGroupBox title="Scope">
            <div className="mu-field">
              <WfLabel text="Entry form" />
              <WfTextBox value={form.entry} onValueChange={v => set('entry', v)} placeholder="default: the startup form" />
              <WfLabel text="Depth" />
              <WfComboBox items={['0', '1', '2', '3', 'all']} value={form.depth} onSelectionChange={v => set('depth', v)} />
            </div>
            <WfCheckBox text="Every form and user control (--units all)" checked={form.allUnits} onCheckedChange={v => set('allUnits', v)} />
            <WfCheckBox text="/modules catalog for units no code opens (--catalog)" checked={form.catalog} onCheckedChange={v => set('catalog', v)} />
            <WfCheckBox text="Do not follow projects outside the source folder" checked={form.noExternalProjects} onCheckedChange={v => set('noExternalProjects', v)} />
          </WfGroupBox>

          <WfGroupBox title="Translation and inputs" disabled={form.mode !== 'app' && form.target !== 'dotnet-api'}>
            <div className="mu-field">
              <WfLabel text="Translate" />
              <WfComboBox items={['none', 'rules', 'llm']} value={form.translate} onSelectionChange={v => set('translate', v)} />
              <WfLabel text="Contract scope" />
              <WfComboBox items={['used', 'all']} value={form.contractScope} onSelectionChange={v => set('contractScope', v)} />
              <WfLabel text="Contract files" />
              <WfTextBox multiline value={form.contracts} onValueChange={v => set('contracts', v)} placeholder="one contract.json path per line" />
            </div>
            <WfCheckBox text="Compile gate (installs npm packages, runs tsc / ngc)" checked={form.compileGate} onCheckedChange={v => set('compileGate', v)} />
            <WfCheckBox text="Call the generated .NET API for data access (--api)" checked={form.api} onCheckedChange={v => set('api', v)} />
            <div className="mu-field">
              <WfLabel text="API database" />
              <WfComboBox items={['sqlclient', 'oledb']} value={form.dbProvider} onSelectionChange={v => set('dbProvider', v)} />
            </div>
            <div className="mu-hint">Generate the server with target dotnet-api (same source); its provider is the API database above.</div>
          </WfGroupBox>

          <WfGroupBox title="Rules and images">
            <div className="mu-field">
              <WfLabel text="Images folder" />
              <div className="mu-row"><WfTextBox value={form.images} onValueChange={v => set('images', v)} /><WfButton text="Browse…" onClick={() => void pick('images', 'Images folder')} /></div>
              <WfLabel text="Archetype rules" />
              <WfTextBox value={form.archetypes} onValueChange={v => set('archetypes', v)} placeholder="control-archetypes.json copy (optional)" />
            </div>
          </WfGroupBox>

          <div className="mu-cmd">{preview(form)}</div>
          <div className="mu-row">
            <WfButton text="&Run" variant="primary" disabled={!info?.cliAvailable || migrating || !form.source || !form.out} onClick={() => void go()} />
            <WfButton text="Cancel" disabled={!migrating} onClick={() => run && void stop(run.id)} />
            {migrating && <WfProgress indeterminate />}
          </div>
          {error && <span className="mu-status-bad" role="alert">{error}</span>}
        </div>

        <div className="mu-result">
          {current && (
            <div className="mu-row">
              <strong>{current.title}</strong>
              <span className={current.done ? (current.exitCode === 0 ? 'mu-status-ok' : 'mu-status-bad') : ''}>
                {current.done ? (current.cancelled ? 'cancelled' : current.exitCode === 0 ? 'finished' : `failed (exit ${current.exitCode ?? '?'})`) : 'running…'}
              </span>
              {current.kind === 'dev' && current.ready && !current.done && <a href={current.url} target="_blank" rel="noreferrer">Open {current.url}</a>}
            </div>
          )}
          <div className="mu-tabs">
            <WfTabs selectedIndex={tab} onSelectionChange={setTab}>
              <WfTabPage title="Log"><pre className="mu-log" ref={logEnd}>{log.length ? log.join('\n') : 'Choose a source and an output folder, then press Run.'}</pre></WfTabPage>
              <WfTabPage title="Report"><pre className="mu-report">{report || (isApp ? 'The migration report appears here when a run finishes.' : 'Only the working-app mode writes a migration report.')}</pre></WfTabPage>
              <WfTabPage title="Run the app">
                <div className="mu-result">
                  {!appDir && <span className="mu-hint">Generate something first; its folder and run command appear here.</span>}
                  {appDir && (
                    <>
                      <WfLabel text={`Folder: ${appDir}`} />
                      {lastMigration && lastMigration.title.startsWith('IR only') && <span className="mu-hint">IR files only: nothing to run.</span>}
                      {lastMigration && !lastMigration.title.startsWith('IR only') && (
                        <>
                          <div className="mu-cmd">{command}</div>
                          <div className="mu-row">
                            <WfButton text="Copy command" onClick={() => copy(command)} />
                            <WfButton text="1. Install" disabled={!!dev} onClick={() => void act(installApp, appDir)} />
                            <WfButton text="2. Start" variant="primary" disabled={!!dev} onClick={() => void act(startApp, appDir)} />
                            <WfButton text="Stop" disabled={!dev} onClick={() => dev && void stop(dev.id)} />
                            <WfButton text="Open in browser" variant="primary" disabled={!dev?.ready} onClick={() => window.open(dev!.url, '_blank')} />
                          </div>
                          <span className="mu-hint">{dev ? (dev.ready ? `Running at ${dev.url}` : `Starting on port ${dev.port}…`) : 'Not running.'} Routes: / the entry form, /pages the list, /modules the catalog (with --catalog or --units all).</span>
                        </>
                      )}
                    </>
                  )}
                </div>
              </WfTabPage>
              <WfTabPage title="Runs">
                <ul>
                  {runs.map(r => (
                    <li key={r.id}>
                      <a href="#" onClick={e => { e.preventDefault(); follow(r); }}>{r.title}</a> — {r.done ? `exit ${r.exitCode}` : 'running'}{r.url && !r.done ? ` — ${r.url}` : ''}
                      {!r.done && <> <a href="#" onClick={e => { e.preventDefault(); void stop(r.id); }}>stop</a></>}
                    </li>
                  ))}
                  {runs.length === 0 && <li className="mu-hint">Nothing has run yet.</li>}
                </ul>
              </WfTabPage>
            </WfTabs>
          </div>
        </div>
      </div>
    </div>
  );
}
