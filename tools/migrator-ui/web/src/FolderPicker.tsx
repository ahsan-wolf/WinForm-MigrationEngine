import { useCallback, useEffect, useState } from 'react';
import { WfButton, WfLabel, WfListBox, WfTextBox } from '@wfm/ui-react';
import { browse, type Browse } from './api';

/** A folder chooser (the browser cannot give a real path): the host lists the folders; selecting an entry opens it. */
export function FolderPicker({ initial, onDone }: { initial: string; onDone: (path?: string) => void }) {
  const [location, setLocation] = useState<Browse>({ path: initial, parent: null, dirs: [], files: [] });
  const [typed, setTyped] = useState(initial);
  const [error, setError] = useState('');

  const open = useCallback((path: string) => {
    browse(path).then(b => { setLocation(b); setTyped(b.path); setError(''); }).catch((e: Error) => setError(e.message));
  }, []);
  useEffect(() => { open(initial); }, [initial, open]);

  const join = (name: string) => location.path.replace(/[\\/]+$/, '') + (location.path.includes('/') && !location.path.includes('\\') ? '/' : '\\') + name;

  return (
    <div className="mu-picker">
      <div className="mu-row">
        <WfButton text="&Up" disabled={location.parent === null || location.path === ''} onClick={() => open(location.parent ?? '')} />
        <WfTextBox value={typed} onValueChange={setTyped} onEnter={() => open(typed)} />
        <WfButton text="&Go" onClick={() => open(typed)} />
      </div>
      {error && <span className="mu-status-bad" role="alert">{error}</span>}
      {location.files.length > 0 && <WfLabel text={`Contains: ${location.files.join(', ')}`} />}
      <WfListBox items={location.dirs} onSelectionChange={name => { if (name) open(location.path === '' ? name : join(name)); }} />
      <div className="mu-row">
        <WfButton text="&Select this folder" variant="primary" disabled={location.path === ''} onClick={() => onDone(location.path)} />
        <WfButton text="Cancel" onClick={() => onDone(undefined)} />
      </div>
    </div>
  );
}
