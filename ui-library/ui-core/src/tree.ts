/** One node of a <WfTreeView> (WinForms TreeNode). The tree is plain data: pages keep it in their model and code adds, removes and edits nodes in place. */
export interface WfTreeNode {
  text: string;
  /** TreeNode.Name (the lookup key). */
  name?: string;
  /** TreeNode.Tag: any value the application attached. */
  tag?: unknown;
  checked?: boolean;
  expanded?: boolean;
  /** A web path, URL or data URI shown before the text. Wins over `imageKey` / `imageIndex`. */
  icon?: string;
  /** TreeNode.ImageKey / ImageIndex: the image in the tree's `imageList` shown before the text. */
  imageKey?: string;
  imageIndex?: number;
  /** The node has children that are not loaded yet; expanding it calls `onNodeExpand` so the page can fill `nodes` (WinForms lazy loading in BeforeExpand). */
  hasChildren?: boolean;
  nodes: WfTreeNode[];
}

/** `new TreeNode(text, children)`. */
export function wfTreeNode(text = '', nodes: WfTreeNode[] = []): WfTreeNode {
  return { text, tag: undefined, checked: false, expanded: false, nodes };
}

/** `ExpandAll()` / `CollapseAll()` over a list of nodes and everything below them. */
export function wfSetExpanded(nodes: WfTreeNode[], expanded: boolean): void {
  for (const node of nodes) {
    node.expanded = expanded;
    wfSetExpanded(node.nodes, expanded);
  }
}
