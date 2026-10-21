import { Component, input, type Type } from '@angular/core';
import { NgComponentOutlet } from '@angular/common';

/** One control a page adds to a container at run time (WinForms `panel.Controls.Add(new SomeControl(args))`). */
export interface WfDynamicControl {
  key: number;
  component: Type<unknown>;
  props: Record<string, unknown>;
}

let nextKey = 1;

/** The counterpart of `new SomeControl(args)` for a container's `controls` list; the props are the control's constructor arguments by name (its inputs). */
export function wfDynamicControl(component: Type<unknown>, props?: Record<string, unknown>): WfDynamicControl {
  return { key: nextKey++, component, props: props ?? {} };
}

/** Renders the controls a page added to a container in code, in order. Add and remove entries of `controls` (a model array) to change what shows. */
@Component({
  selector: 'wf-dynamic-host',
  standalone: true,
  imports: [NgComponentOutlet],
  template: `<div [class]="className() ? 'wf-dynamic ' + className() : 'wf-dynamic'" [attr.id]="controlId()">
      @for (c of controls(); track c.key) { <ng-container *ngComponentOutlet="c.component; inputs: c.props" /> }
    </div>`,
})
export class WfDynamicHost {
  controls = input<WfDynamicControl[]>([]);
  controlId = input<string | undefined>();
  className = input<string | undefined>();
}
