// Calls of the local host (tools/migrator-ui/host). Every POST carries X-Migrator-UI: the host refuses requests without it.

export interface Info { repoRoot: string | null; enginesJson: string | null; cliAvailable: boolean; uiBuilt: boolean; platform: string; targets: { id: string; modes: string[] }[] }
export interface RunInfo {
  id: string; kind: 'migrate' | 'install' | 'dev'; title: string; commandLine: string; outputDirectory: string | null; appDirectory: string | null;
  done: boolean; exitCode: number | null; cancelled: boolean; port: number | null; ready: boolean; url: string; lines: number; started: string;
}
export interface Browse { path: string; parent: string | null; dirs: string[]; files: string[] }
export interface MigrateForm {
  source: string; out: string; mode: 'ir' | 'app' | 'appshell' | 'viewer'; target: string; entry: string; depth: string; allUnits: boolean; catalog: boolean;
  noExternalProjects: boolean; translate: string; compileGate: boolean; contracts: string; contractScope: string; images: string; archetypes: string; api: boolean; dbProvider: string;
}

async function check(res: Response): Promise<Response> {
  if (res.ok) return res;
  let message = res.statusText;
  try { const body = await res.json() as { error?: string }; if (body.error) message = body.error; } catch { /* not JSON */ }
  throw new Error(message);
}

const post = (url: string, body: unknown) =>
  fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Migrator-UI': '1' }, body: JSON.stringify(body) }).then(check);

export const getInfo = (): Promise<Info> => fetch('api/info').then(check).then(r => r.json());
export const browse = (path: string): Promise<Browse> => fetch('api/browse?path=' + encodeURIComponent(path)).then(check).then(r => r.json());
export const listRuns = (): Promise<RunInfo[]> => fetch('api/runs').then(check).then(r => r.json());
export const getReport = (dir: string): Promise<string> => fetch('api/report?dir=' + encodeURIComponent(dir)).then(check).then(r => r.text());

export function startMigration(f: MigrateForm): Promise<RunInfo> {
  return post('api/migrate', {
    source: f.source, out: f.out, mode: f.mode, target: f.target, entry: f.entry, depth: f.depth, allUnits: f.allUnits, catalog: f.catalog,
    noExternalProjects: f.noExternalProjects, translate: f.translate, compileGate: f.compileGate, contractScope: f.contractScope, images: f.images, archetypes: f.archetypes, api: f.api, dbProvider: f.dbProvider,
    contracts: f.contracts.split(/\r?\n/).map(s => s.trim()).filter(Boolean),
  }).then(r => r.json());
}
export const installApp = (dir: string): Promise<RunInfo> => post('api/app/install', { dir }).then(r => r.json());
export const startApp = (dir: string): Promise<RunInfo> => post('api/app/start', { dir }).then(r => r.json());
export const stopRun = (id: string): Promise<RunInfo> => post(`api/runs/${id}/stop`, {}).then(r => r.json());

/** Streams the output of a run; `onDone` fires once the process has ended and every line was delivered. */
export function followLog(id: string, onLine: (line: string) => void, onDone: () => void): () => void {
  const source = new EventSource(`api/runs/${id}/log`);
  source.onmessage = e => onLine(JSON.parse(e.data) as string);
  source.addEventListener('done', () => { source.close(); onDone(); });
  source.onerror = () => { source.close(); onDone(); };
  return () => source.close();
}
