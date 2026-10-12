import { useMemo, useState, type CSSProperties } from 'react';
import {
  MEMBER_KIND, VISIBILITY, eventAttributes, flatten, shortType,
  type ControlLayout, type ControlNode, type LoadedUnit, type Track,
} from './ir';

const lower = (s: string | undefined): string => (s ?? '').toLowerCase();

// ---------------------------------------------------------------------------------------------- Control tree

function matches(n: ControlNode, q: string): boolean {
  return lower(n.Id).includes(q) || lower(n.TagName).includes(q) || lower(n.Attributes.Text).includes(q)
    || n.Children.some(c => matches(c, q));
}

function TreeNode({ node, q, depth }: { node: ControlNode; q: string; depth: number }) {
  const children = q ? node.Children.filter(c => matches(c, q)) : node.Children;
  const slot = node.Attributes.ParentSlot;
  const label = (
    <span className="node-row">
      <b>{node.Id || '(unnamed)'}</b> <span className="type">{shortType(node.TagName)}</span>
      {slot && <span className="tag">{slot}</span>}
      {node.Attributes.Text && <span className="text">“{node.Attributes.Text}”</span>}
      {eventAttributes(node).map(([k, v]) => <span key={k} className="tag ev">{k.slice(2)} → {v}</span>)}
      {node.Semantic && <span className="sem">{node.Semantic}</span>}
    </span>
  );
  if (children.length === 0) return <div className="leaf">{label}</div>;
  return (
    <details open={depth < 2 || q !== ''}>
      <summary>{label}</summary>
      <div className="indent">{children.map(c => <TreeNode key={c.Id + c.TagName} node={c} q={q} depth={depth + 1} />)}</div>
    </details>
  );
}

export function TreeView({ unit }: { unit: LoadedUnit }) {
  const [q, setQ] = useState('');
  const root = unit.page.RootControl;
  if (!root) return <p className="empty">No control tree in this unit.</p>;
  return (
    <>
      <input className="search" placeholder="Filter by id, type or text…" value={q} onChange={e => setQ(lower(e.target.value))} />
      <TreeNode node={root} q={q} depth={0} />
    </>
  );
}

// ---------------------------------------------------------------------------------------------------- Layout

const STRIPS = new Set(['MenuStrip', 'ToolStrip', 'StatusStrip', 'ContextMenuStrip']);
const NON_VISUAL_SLOTS = new Set(['Items', 'DropDownItems', 'Columns']);

const trackCss = (t: Track): string =>
  t.SizeType === 'Percent' ? `${t.Value ?? 1}fr` : t.SizeType === 'Absolute' ? `${t.Value ?? 0}px` : 'auto';

function boxStyle(l: ControlLayout | undefined, inGrid: boolean): CSSProperties {
  if (!l) return {};
  const s: CSSProperties = {};
  if (inGrid && l.TableLayout.Row !== null && l.TableLayout.Column !== null) {
    s.gridRow = `${l.TableLayout.Row + 1} / span ${l.TableLayout.RowSpan ?? 1}`;
    s.gridColumn = `${l.TableLayout.Column + 1} / span ${l.TableLayout.ColumnSpan ?? 1}`;
    return s;
  }
  const size = l.Size;
  switch (l.Dock) {
    case 'Top': return { position: 'absolute', left: 0, right: 0, top: 0, height: size?.Height ?? 40 };
    case 'Bottom': return { position: 'absolute', left: 0, right: 0, bottom: 0, height: size?.Height ?? 40 };
    case 'Left': return { position: 'absolute', left: 0, top: 0, bottom: 0, width: size?.Width ?? 120 };
    case 'Right': return { position: 'absolute', right: 0, top: 0, bottom: 0, width: size?.Width ?? 120 };
    case 'Fill': return { position: 'absolute', inset: 0 };
  }
  if (l.Location && size) return { position: 'absolute', left: l.Location.X, top: l.Location.Y, width: size.Width, height: size.Height };
  if (size) { s.width = size.Width; s.height = size.Height; }
  return s;
}

function LayoutBox({ node, layout, inGrid }: { node: ControlNode; layout: Map<string, ControlLayout>; inGrid: boolean }) {
  const l = layout.get(node.Id);
  const type = shortType(node.TagName);
  const hidden = l?.Visible === false;
  const title = node.Attributes.Text ?? '';

  if (STRIPS.has(type)) {
    const items = node.Children.filter(c => c.Attributes.ParentSlot === 'Items');
    return (
      <div className={`lbox strip${hidden ? ' hidden' : ''}`} style={boxStyle(l, inGrid)} title={`${node.Id} (${type})`}>
        <b>{type}</b> {items.map(i => <span key={i.Id} className="tag">{i.Attributes.Text ?? i.Id}</span>)}
      </div>
    );
  }

  const children = node.Children.filter(c => !NON_VISUAL_SLOTS.has(c.Attributes.ParentSlot ?? ''));
  const table = l?.TableDefinition;
  const style: CSSProperties = { ...boxStyle(l, inGrid) };
  if (table) {
    style.display = 'grid';
    style.gridTemplateColumns = table.Columns.map(trackCss).join(' ');
    style.gridTemplateRows = table.Rows.map(trackCss).join(' ');
  }
  return (
    <div className={`lbox ${children.length ? 'container' : 'leafbox'}${hidden ? ' hidden' : ''}`} style={style}
         title={`${node.Id} (${type})${l?.Anchor ? ' anchor ' + l.Anchor.join('|') : ''}${l?.Dock ? ' dock ' + l.Dock : ''}`}>
      <span className="cap">{type}: {title || node.Id}</span>
      {children.map(c => <LayoutBox key={c.Id + c.TagName} node={c} layout={layout} inGrid={table !== undefined && table !== null} />)}
    </div>
  );
}

export function LayoutView({ unit }: { unit: LoadedUnit }) {
  const root = unit.page.RootControl;
  if (!root) return <p className="empty">No control tree in this unit.</p>;
  const size = unit.layout.get(root.Id)?.Size ?? { Width: 800, Height: 600 };
  return (
    <>
      <p className="hint">Approximate: drawn from Dock / Location / Size / table cells in the layout facet. Menu items and grid columns are summarized, not drawn.</p>
      <div className="canvas" style={{ width: size.Width, height: size.Height }}>
        {root.Children.filter(c => !NON_VISUAL_SLOTS.has(c.Attributes.ParentSlot ?? '')).map(c =>
          <LayoutBox key={c.Id + c.TagName} node={c} layout={unit.layout} inGrid={false} />)}
      </div>
    </>
  );
}

// --------------------------------------------------------------------------------------------------- Members

export function MembersView({ unit }: { unit: LoadedUnit }) {
  const members = unit.page.Members;
  if (members.length === 0) return <p className="empty">No members.</p>;
  return (
    <table>
      <thead><tr><th>Kind</th><th>Vis</th><th>Type</th><th>Name</th><th>Parameters</th></tr></thead>
      <tbody>
        {members.map((m, i) => (
          <tr key={i}>
            <td>{MEMBER_KIND[m.Kind] ?? m.Kind}</td>
            <td>{VISIBILITY[m.Visibility] ?? m.Visibility}</td>
            <td>{m.Type}</td>
            <td>
              <b>{m.Name}</b>{m.IsStatic && <span className="tag">static</span>}
              {m.Body && <details><summary>body</summary><pre>{m.Body}</pre></details>}
            </td>
            <td>{m.Parameters.map(p => `${p.Type} ${p.Name}`).join(', ')}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

// ------------------------------------------------------------------------------- Workflows and navigation

export function WorkflowsView({ unit }: { unit: LoadedUnit }) {
  if (unit.page.Workflows.length === 0) return <p className="empty">No workflows.</p>;
  return (
    <>
      {unit.page.Workflows.map(w => (
        <div className="card" key={w.Name}>
          <b>{w.TriggerControlId}.{w.TriggerEvent}</b> → {w.HandlerMethod}
          {w.Semantic && <div className="sem">{w.Semantic}</div>}
          {w.Steps.length > 0 && <ol>{w.Steps.map(s => <li key={s.MethodName}>{s.MethodName}{s.Semantic && <span className="sem"> — {s.Semantic}</span>}</li>)}</ol>}
        </div>
      ))}
    </>
  );
}

export function NavigationView({ unit, units, onOpen }: { unit: LoadedUnit; units: Set<string>; onOpen: (name: string) => void }) {
  if (unit.page.Navigations.length === 0) return <p className="empty">No navigation.</p>;
  return (
    <>
      {unit.page.Navigations.map((n, i) => (
        <div className="card" key={i}>
          <b>{n.SourceControlId}</b> <span className="tag">{n.TriggerEvent}</span> <span className="tag ev">{n.TargetType}</span>
          {n.Target && (units.has(n.Target)
            ? <> → <button className="link" onClick={() => onOpen(n.Target)}>{n.Target}</button></>
            : <> → {n.Target} <span className="tag">not in project</span></>)}
        </div>
      ))}
    </>
  );
}

// ------------------------------------------------------------------------------------------------ Properties

export function PropertiesView({ unit }: { unit: LoadedUnit }) {
  const [q, setQ] = useState('');
  const rows = useMemo(() => (unit.page.RootControl ? flatten(unit.page.RootControl) : []), [unit]);
  const shown = q ? rows.filter(n => lower(n.Id).includes(q) || lower(n.TagName).includes(q)) : rows;
  return (
    <>
      <input className="search" placeholder="Filter by id or type…" onChange={e => setQ(lower(e.target.value))} />
      <table>
        <thead><tr><th>Id</th><th>Type</th><th>Properties</th></tr></thead>
        <tbody>
          {shown.map(n => (
            <tr key={n.Id + n.TagName}>
              <td><b>{n.Id}</b></td>
              <td>{shortType(n.TagName)}</td>
              <td className="props">{Object.entries(n.Attributes).map(([k, v]) => <div key={k}><span className="k">{k}</span> = {v}</div>)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
