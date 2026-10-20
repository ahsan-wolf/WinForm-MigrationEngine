import type { ComponentType } from 'react';

/** One control a page adds to a container at run time (WinForms `panel.Controls.Add(new SomeControl(args))`). */
export interface WfDynamicControl {
  key: number;
  component: ComponentType<Record<string, unknown>>;
  props: Record<string, unknown>;
}

let nextKey = 1;

/** The counterpart of `new SomeControl(args)` for a container's `controls` list; the props are the control's constructor arguments by name. */
export function wfDynamicControl<P extends object>(component: ComponentType<P>, props?: P): WfDynamicControl {
  return { key: nextKey++, component: component as unknown as ComponentType<Record<string, unknown>>, props: (props ?? {}) as Record<string, unknown> };
}

export interface WfDynamicHostProps { controls: WfDynamicControl[]; id?: string; className?: string }
/** Renders the controls a page added to a container in code, in order. Add and remove entries of `controls` (a model array) to change what shows. */
export function WfDynamicHost({ controls, id, className }: WfDynamicHostProps) {
  return (
    <div id={id} className={className ? `wf-dynamic ${className}` : 'wf-dynamic'}>
      {controls.map((c) => <c.component key={c.key} {...c.props} />)}
    </div>
  );
}
