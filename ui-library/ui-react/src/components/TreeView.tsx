import { useRef, useState, type KeyboardEvent } from 'react';

const cls = (base: string, extra?: string): string => (extra ? `${base} ${extra}` : base);

/** One node of a <WfTreeView> (WinForms TreeNode). The tree is plain data: pages keep it in their model and code adds, removes and edits nodes in place. */
export interface WfTreeNode {
  text: string;
  /** TreeNode.Name (the lookup key). */
  name?: string;
  /** TreeNode.Tag: any value the application attached. */
  tag?: unknown;
  checked?: boolean;
  expanded?: boolean;
  /** A web path, URL or data URI shown before the text. */
  icon?: string;
  /** The node has children that are not loaded yet; expanding it calls `onNodeExpand` so the page can fill `nodes` (WinForms lazy loading in BeforeExpand). */
  hasChildren?: boolean;
  nodes: WfTreeNode[];
}

/** `new TreeNode(text, children)`. */
export function wfTreeNode(text = '', nodes: WfTreeNode[] = []): WfTreeNode {
  return { text, tag: undefined, checked: false, expanded: false, nodes };
}

/** The tree is plain data owned by the page: opening a node writes `expanded` on it (kept out of the component body so the node stays the page's own object). */
function setNodeExpanded(node: WfTreeNode, expanded: boolean): void {
  node.expanded = expanded;
}

/** `ExpandAll()` / `CollapseAll()` over a list of nodes and everything below them. */
export function wfSetExpanded(nodes: WfTreeNode[], expanded: boolean): void {
  for (const node of nodes) {
    node.expanded = expanded;
    wfSetExpanded(node.nodes, expanded);
  }
}

export interface WfTreeViewProps {
  nodes?: WfTreeNode[];
  /** Controlled selection (WinForms `SelectedNode`); omit for an uncontrolled tree. */
  selectedNode?: WfTreeNode | null;
  checkBoxes?: boolean;
  disabled?: boolean;
  id?: string; className?: string; tooltip?: string;
  onSelectionChange?: (node: WfTreeNode) => void;
  onNodeCheckedChange?: (node: WfTreeNode, checked: boolean) => void;
  onNodeClick?: (node: WfTreeNode) => void;
  onNodeDoubleClick?: (node: WfTreeNode) => void;
  /** Called before a node with `hasChildren` and no loaded children opens (BeforeExpand). May be async: the node shows "Loading…" until it settles. */
  onNodeExpand?: (node: WfTreeNode) => void | Promise<void>;
}

/** Hierarchical tree (TreeView): expand/collapse, single selection, optional check boxes, arrow-key navigation. */
export function WfTreeView({ nodes = [], selectedNode, checkBoxes, disabled, id, className, tooltip, onSelectionChange, onNodeCheckedChange, onNodeClick, onNodeDoubleClick, onNodeExpand }: WfTreeViewProps) {
  const [local, setLocal] = useState<WfTreeNode | null>(null);
  const [, refresh] = useState(0);
  const [loading, setLoading] = useState<ReadonlySet<WfTreeNode>>(new Set());
  const root = useRef<HTMLUListElement>(null);
  const selected = onSelectionChange ? selectedNode ?? null : local;

  const select = (node: WfTreeNode) => { setLocal(node); onSelectionChange?.(node); onNodeClick?.(node); };
  const toggle = (node: WfTreeNode) => {
    if (!node.expanded && node.hasChildren && node.nodes.length === 0 && onNodeExpand) {
      setLoading(s => new Set(s).add(node));
      setNodeExpanded(node, true);
      void Promise.resolve(onNodeExpand(node)).finally(() => { setLoading(s => { const n = new Set(s); n.delete(node); return n; }); refresh(n => n + 1); });
      return;
    }
    setNodeExpanded(node, !node.expanded);
    refresh(n => n + 1);
  };
  const rows = () => Array.from(root.current?.querySelectorAll<HTMLElement>('.wf-tree-row') ?? []);

  const onKeyDown = (e: KeyboardEvent<HTMLElement>, node: WfTreeNode) => {
    const all = rows();
    const at = all.indexOf(e.currentTarget);
    if (e.key === 'ArrowDown') all[at + 1]?.focus();
    else if (e.key === 'ArrowUp') all[at - 1]?.focus();
    else if (e.key === 'ArrowRight' && node.nodes.length && !node.expanded) toggle(node);
    else if (e.key === 'ArrowLeft' && node.nodes.length && node.expanded) toggle(node);
    else if (e.key === 'Enter' || e.key === ' ') select(node);
    else return;
    e.preventDefault();
  };

  const render = (list: WfTreeNode[]) => list.map((node, i) => {
    const hasChildren = node.nodes.length > 0 || !!node.hasChildren;
    return (
      <li key={`${i}:${node.text}`} role="treeitem" aria-expanded={hasChildren ? !!node.expanded : undefined} aria-selected={selected === node}>
        <div className={cls('wf-tree-row', selected === node ? 'wf-tree-selected' : undefined)} tabIndex={disabled ? -1 : 0}
             onClick={() => !disabled && select(node)} onDoubleClick={() => !disabled && onNodeDoubleClick?.(node)} onKeyDown={e => !disabled && onKeyDown(e, node)}>
          <span className="wf-tree-toggle" aria-hidden onClick={e => { e.stopPropagation(); if (hasChildren && !disabled) toggle(node); }}>{hasChildren ? (node.expanded ? '▾' : '▸') : ''}</span>
          {checkBoxes && <input type="checkbox" tabIndex={-1} disabled={disabled} checked={!!node.checked} onClick={e => e.stopPropagation()}
                                onChange={e => { node.checked = e.target.checked; refresh(n => n + 1); onNodeCheckedChange?.(node, e.target.checked); }} />}
          {node.icon && <img src={node.icon} alt="" className="wf-tree-icon" />}
          <span className="wf-tree-text">{node.text}</span>
        </div>
        {hasChildren && node.expanded && <ul role="group">{loading.has(node) ? <li className="wf-tree-loading" role="none">Loading…</li> : render(node.nodes)}</ul>}
      </li>
    );
  });

  return <ul ref={root} role="tree" id={id} title={tooltip} aria-disabled={disabled} className={cls('wf-tree', className)}>{render(nodes)}</ul>;
}
