using System.Text.Json;

namespace Migrator.Target.React.Generation;

/// <summary>Deterministic files around the generated pages: package, tooling config, entry point, base CSS. No LLM, no per-form logic.</summary>
public static class AppWriter
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <param name="entryComponent">The entry form's component; null for the viewer app, which writes its own entry point and needs no C# helpers.</param>
    public static void Write(string appDir, string appName, string? entryComponent, string uiLibraryDir, List<string> files)
    {
        var uiRelative = Path.GetRelativePath(appDir, uiLibraryDir).Replace('\\', '/');
        var package = new Dictionary<string, object>
        {
            ["name"] = appName,
            ["private"] = true,
            ["version"] = "0.0.0",
            ["type"] = "module",
            ["scripts"] = new Dictionary<string, string>
            {
                ["dev"] = "vite", ["build"] = "tsc --noEmit && vite build", ["lint"] = "eslint .", ["preview"] = "vite preview"
            },
            ["dependencies"] = new Dictionary<string, string>
            {
                ["@wfm/ui-react"] = "file:" + uiRelative, ["react"] = "^18.3.1", ["react-dom"] = "^18.3.1"
            },
            ["devDependencies"] = new Dictionary<string, string>
            {
                ["@eslint/js"] = "^10.0.1", ["@types/react"] = "^18.3.31", ["@types/react-dom"] = "^18.3.7",
                ["@vitejs/plugin-react"] = "^6.1.2", ["eslint"] = "^10.12.0", ["eslint-plugin-react-hooks"] = "^7.1.1",
                ["globals"] = "^17.13.0", ["typescript"] = "^6.0.3", ["typescript-eslint"] = "^8.71.1", ["vite"] = "^8.3.4"
            }
        };
        Put(appDir, "package.json", JsonSerializer.Serialize(package, Pretty) + "\n", files);

        Put(appDir, "index.html", $$"""
            <!doctype html>
            <html lang="en">
              <head>
                <meta charset="UTF-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>{{appName}}</title>
              </head>
              <body>
                <div id="root"></div>
                <script type="module" src="/src/main.tsx"></script>
              </body>
            </html>
            """, files);

        Put(appDir, "tsconfig.json", """
            {
              "compilerOptions": {
                "target": "ES2022",
                "lib": ["ES2022", "DOM", "DOM.Iterable"],
                "module": "ESNext",
                "moduleResolution": "bundler",
                "jsx": "react-jsx",
                "strict": true,
                "isolatedModules": true,
                "skipLibCheck": true,
                "noEmit": true,
                "types": ["vite/client"],
                "paths": { "react": ["./node_modules/@types/react"], "react/*": ["./node_modules/@types/react/*"] }
              },
              "include": ["src", "vite.config.ts"]
            }
            """, files);

        // The UI library is a linked local package with its own React copy: force a single React for the app.
        Put(appDir, "vite.config.ts", """
            import { defineConfig } from 'vite';
            import react from '@vitejs/plugin-react';

            export default defineConfig({
              plugins: [react()],
              resolve: { dedupe: ['react', 'react-dom'] },
            });
            """, files);

        Put(appDir, "eslint.config.js", """
            import js from '@eslint/js';
            import globals from 'globals';
            import reactHooks from 'eslint-plugin-react-hooks';
            import tseslint from 'typescript-eslint';

            export default tseslint.config(
              { ignores: ['dist'] },
              {
                files: ['**/*.{ts,tsx}'],
                extends: [js.configs.recommended, ...tseslint.configs.recommended],
                languageOptions: { globals: globals.browser },
                plugins: { 'react-hooks': reactHooks },
                rules: {
                  ...reactHooks.configs.recommended.rules,
                  '@typescript-eslint/no-unused-vars': ['error', { args: 'none', argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_' }],
                  // Stylistic rules that translated C# trips on without any bug: `!!x` kept from `!string.IsNullOrEmpty(x)`, and rethrown errors.
                  'no-extra-boolean-cast': 'off',
                  // Faithfully translated C# has initial values that are overwritten, declarations assigned elsewhere (out parameters), rethrows and constant loops.
                  'no-useless-assignment': 'off',
                  'no-unassigned-vars': 'off',
                  'no-useless-catch': 'off',
                  'no-constant-condition': 'off',
                  'preserve-caught-error': 'off',
                  // useWfModel returns a deliberately mutable model (WinForms-style imperative code: txt.Text = x). Do not enable the React Compiler here.
                  'react-hooks/immutability': 'off',
                },
              },
            );
            """, files);

        if (entryComponent is not null)
        {
        Put(appDir, "src/main.tsx", $$"""
            import { Component, StrictMode, Suspense, lazy, type ComponentType, type ReactNode } from 'react';
            import { createRoot } from 'react-dom/client';
            import { WfDialogProvider } from '@wfm/ui-react';
            import '@wfm/ui-react/styles.css';
            import './app.css';
            import { {{entryComponent}} } from './pages/{{entryComponent}}';

            // Development launcher: `?page=<Name>` opens one generated page on its own (with mock data), `?page=` lists them all.
            // Forms the entry form opens through ShowDialog() are normally reached from it, but those whose constructor arguments
            // are not translated yet (see MIGRATION_REPORT.md) cannot be reached that way.
            type PageProps = { onClose?: (result?: unknown) => void };
            const loaders = import.meta.glob('./pages/*.tsx') as Record<string, () => Promise<Record<string, unknown>>>;
            const pages = Object.entries(loaders).map(([path, load]) => {
              const name = path.slice('./pages/'.length, -'.tsx'.length);
              return { name, Page: lazy(async () => ({ default: (await load())[name] as ComponentType<PageProps> })) };
            });
            const requested = new URLSearchParams(window.location.search).get('page');
            const selected = requested === null ? undefined : pages.find((p) => p.name === requested || p.name === requested + 'Page');

            // A page that throws while rendering shows its error instead of a blank screen.
            class Boundary extends Component<{ children: ReactNode }, { error: Error | null }> {
              state = { error: null as Error | null };
              static getDerivedStateFromError(error: Error) { return { error }; }
              render() { return this.state.error ? <pre role="alert">{String(this.state.error.stack ?? this.state.error)}</pre> : this.props.children; }
            }

            const goHome = () => { window.location.search = ''; };

            function Index() {
              return (
                <ul>
                  {pages.map((p) => <li key={p.name}><a href={`?page=${p.name}`}>{p.name}</a></li>)}
                </ul>
              );
            }

            const app = selected ? (
              <Suspense fallback={null}><selected.Page onClose={goHome} /></Suspense>
            ) : requested !== null ? <Index /> : <{{entryComponent}} />;

            // The entry form is the app shell; every ShowDialog() target opens through the dialog service.
            createRoot(document.getElementById('root')!).render(
              <StrictMode>
                <WfDialogProvider><Boundary>{app}</Boundary></WfDialogProvider>
              </StrictMode>,
            );
            """, files);

        // Runtime helpers for translated C# (string.Format with a non-literal format string).
        Put(appDir, "src/lib/csharp.ts", """
            // Helpers used by code translated from C#. Format specifiers ({0:N2}) are not interpreted: values are converted with String().
            export function formatString(format: string, ...args: unknown[]): string {
              return format.replace(/\{(\d+)(?::[^}]*)?\}/g, (_match, index: string) => String(args[Number(index)] ?? ''));
            }
            """, files);
        }

        // Layout helpers used by generated pages (flex rows/columns derived from Dock and Location; never pixels).
        Put(appDir, "src/app.css", """
            html, body, #root { height: 100%; margin: 0; }
            .wfm-body { display: flex; flex-direction: column; gap: 8px; padding: 8px; flex: 1 1 auto; min-height: 0; overflow: auto; }
            .wfm-row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
            .wfm-main { display: flex; gap: 8px; flex: 1 1 auto; min-height: 0; }
            .wfm-fill { flex: 1 1 auto; min-width: 0; min-height: 0; display: flex; flex-direction: column; }
            """, files);
    }

    public static void PutBytes(string dir, string relativePath, byte[] data, List<string> files)
    {
        var path = Path.Combine(dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        files.Add(path);
    }

    public static void PutAssets(string dir, GeneratedPage page, List<string> files)
    {
        foreach (var (path, data) in page.Assets) PutBytes(dir, path, data, files);
    }

    public static void Put(string dir, string relativePath, string content, List<string> files)
    {
        var path = Path.Combine(dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.EndsWith('\n') ? content : content + "\n");
        files.Add(path);
    }
}
