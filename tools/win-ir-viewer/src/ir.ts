// Types and loaders for the IR written by `migrator ir` (Migrator.Engine IrFileWriter). DOM-free on purpose.

export interface UnitRef { Name: string; Kind: string; IrFile: string }

export interface ProjectIr {
  SchemaVersion: string;
  Source: { Technology: string; FrameworkVersion: string | null; RootPath: string; StartupForm: string | null };
  Forms: UnitRef[];
  UserControls: UnitRef[];
  Shared: { Settings: string[]; Resources: string[]; Dependencies: string[]; Unsupported: string[] };
  Failures: { Name: string; Kind: string; Error: string }[];
}

export interface ControlNode {
  Id: string;
  TagName: string;
  Attributes: Record<string, string>;
  Children: ControlNode[];
  Semantic?: string | null;
}

export interface Member {
  Kind: number;
  Name: string;
  Type: string;
  Visibility: number;
  IsStatic: boolean;
  Parameters: { Name: string; Type: string; DefaultValue?: string | null }[];
  Body?: string | null;
  Attributes?: string[];
}

export interface Workflow {
  Name: string;
  TriggerControlId: string;
  TriggerEvent: string;
  HandlerMethod: string;
  Steps: { MethodName: string; Semantic?: string | null }[];
  Semantic?: string | null;
}

/** WinForms navigation rides on the shared NavigationIr: TargetType is Show | ShowDialog | Close | Hide | Exit. */
export interface Navigation {
  SourceControlId: string;
  TriggerEvent: string;
  Target: string;
  TargetType: string;
  Semantic?: string | null;
}

export interface PageIr {
  Name: string;
  RelativePath?: string | null;
  RootControl?: ControlNode | null;
  Members: Member[];
  Workflows: Workflow[];
  Navigations: Navigation[];
}

export interface ControlLayout {
  Id: string;
  Dock: string | null;
  Anchor: string[] | null;
  Location: { X: number; Y: number } | null;
  Size: { Width: number; Height: number } | null;
  TabIndex: number | null;
  Margin: Insets | null;
  Padding: Insets | null;
  Visible: boolean | null;
  Enabled: boolean | null;
  TableLayout: { Row: number | null; Column: number | null; RowSpan: number | null; ColumnSpan: number | null };
  TableDefinition?: { Columns: Track[]; Rows: Track[] } | null;
}
export interface Insets { Left: number; Top: number; Right: number; Bottom: number }
export interface Track { SizeType: string; Value: number | null }

export const MEMBER_KIND = ['Property', 'Field', 'Method', 'Event'] as const;
export const VISIBILITY = ['public', 'protected', 'private', 'internal'] as const;

/** A folder of IR files, addressed by '/'-separated relative paths. */
export interface IrSource {
  paths(): string[];
  readText(path: string): Promise<string | undefined>;
}

export interface LoadedProject { project: ProjectIr; base: string; source: IrSource }
export interface LoadedUnit { page: PageIr; layout: Map<string, ControlLayout> }

const PROJECT_FILE = 'project.ir.json';

export async function loadProject(source: IrSource): Promise<LoadedProject> {
  const path = source.paths()
    .filter(p => p === PROJECT_FILE || p.endsWith('/' + PROJECT_FILE))
    .sort((a, b) => a.length - b.length)[0];
  if (!path) throw new Error(`${PROJECT_FILE} not found. Pick the folder that contains it (the "ir" output folder).`);

  const text = await source.readText(path);
  if (!text) throw new Error(`Cannot read ${path}.`);
  return { project: JSON.parse(text) as ProjectIr, base: path.slice(0, path.length - PROJECT_FILE.length), source };
}

export async function loadUnit(p: LoadedProject, ref: UnitRef): Promise<LoadedUnit> {
  const completePath = p.base + ref.IrFile;
  const text = await p.source.readText(completePath);
  if (!text) throw new Error(`Missing ${completePath}`);

  const layoutText = await p.source.readText(completePath.replace(/\.complete\.ir\.json$/, '.layout.ir.json'));
  const layout = new Map<string, ControlLayout>();
  if (layoutText) for (const c of (JSON.parse(layoutText) as { Controls: ControlLayout[] }).Controls) layout.set(c.Id, c);

  return { page: JSON.parse(text) as PageIr, layout };
}

export const allUnits = (p: ProjectIr): UnitRef[] => [...p.Forms, ...p.UserControls];

export const shortType = (tag: string): string => tag.slice(tag.lastIndexOf('.') + 1);

export function countControls(n: ControlNode | null | undefined): number {
  return n ? 1 + n.Children.reduce((sum, c) => sum + countControls(c), 0) : 0;
}

/** Event wiring is stored as Attributes["OnClick"] = "handler". */
export function eventAttributes(n: ControlNode): [string, string][] {
  return Object.entries(n.Attributes).filter(([k]) => /^On[A-Z]/.test(k));
}

export function flatten(n: ControlNode, out: ControlNode[] = []): ControlNode[] {
  out.push(n);
  n.Children.forEach(c => flatten(c, out));
  return out;
}
