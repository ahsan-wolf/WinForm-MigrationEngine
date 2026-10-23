import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { WfDialogProvider } from '@wfm/ui-react';
import '@wfm/ui-react/styles.css';
import './app.css';
import { App } from './App';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <WfDialogProvider><App /></WfDialogProvider>
  </StrictMode>,
);
