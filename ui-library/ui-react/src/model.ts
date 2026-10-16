import { useReducer, useState } from 'react';

type Notify = () => void;

/** Wrapper -> wrapped object, so storing a model object elsewhere in the model keeps ONE identity (`vm.tv.selectedNode = vm.tv.nodes[0]` is `===` the node). */
const raw = new WeakMap<object, object>();

/** Wraps an object (and, lazily, every nested object/array) so that writes mark the model dirty. Wrappers are cached per target. */
function wrap<T extends object>(target: T, notify: Notify, cache: WeakMap<object, object>): T {
  const cached = cache.get(target);
  if (cached) return cached as T;

  const proxy = new Proxy(target, {
    get(obj, key, receiver) {
      const value = Reflect.get(obj, key, receiver) as unknown;
      // Only plain objects and arrays are wrapped; Dates, Maps, class instances etc. are returned untouched.
      if (value !== null && typeof value === 'object' && (Array.isArray(value) || Object.getPrototypeOf(value) === Object.prototype))
        return wrap(value as object, notify, cache);
      return value;
    },
    set(obj, key, value, receiver) {
      const stored = value !== null && typeof value === 'object' ? raw.get(value as object) ?? value : value;
      const ok = Reflect.set(obj, key, stored, receiver);
      notify();
      return ok;
    },
    deleteProperty(obj, key) {
      const ok = Reflect.deleteProperty(obj, key);
      notify();
      return ok;
    },
  });
  cache.set(target, proxy);
  raw.set(proxy, target);
  return proxy;
}

/**
 * Imperative-compatible component state. Reads return the current value immediately and writes are visible to the next read in the same
 * handler, then the component re-renders once (microtask-batched). This keeps WinForms-style code (`txt.Text = x; Run(txt.Text)`) a
 * 1:1 translation: no stale closures, no setters to invent.
 *
 *   const vm = useWfModel({ txtSearch: { text: '', enabled: true }, lvFile: { items: [] as string[] } });
 *   vm.txtSearch.text = 'abc';          // re-renders
 *   vm.lvFile.items.push('a.pdf');      // re-renders
 *
 * The initial value is only used on the first render. Keep values plain (objects, arrays, primitives); replace a nested object to reset it.
 */
export function useWfModel<T extends object>(initial: T): T {
  const [, force] = useReducer((n: number) => n + 1, 0);
  const [model] = useState(() => {
    let scheduled = false;
    const notify: Notify = () => {
      if (scheduled) return;
      scheduled = true;
      queueMicrotask(() => {
        scheduled = false;
        force();
      });
    };
    return wrap(initial, notify, new WeakMap());
  });
  return model;
}
