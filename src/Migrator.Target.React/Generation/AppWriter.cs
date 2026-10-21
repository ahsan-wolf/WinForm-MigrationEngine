using System.Text.Json;
using Migrator.Target.Core.Ir;
using Migrator.Target.Core.Translation;

namespace Migrator.Target.React.Generation;

/// <summary>Deterministic files around the generated pages: package, tooling config, entry point, base CSS. No LLM, no per-form logic.</summary>
public static class AppWriter
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <param name="entryComponent">The entry form's component; null for the viewer app, which writes its own entry point and needs no C# helpers.</param>
    public static void Write(string appDir, string appName, string? entryComponent, string uiLibraryDir, List<string> files, string? favicon = null)
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
                {{FaviconLink(favicon)}}
                <!-- Relative URLs (icons/…, images/…, route links) resolve against the app base, so the app runs at the site root or under a sub-path. -->
                <base href="%BASE_URL%" />
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

            // A relative base: built assets and the <base href> in index.html work wherever the app is hosted (a host must serve index.html for unknown paths).
            export default defineConfig({
              base: './',
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
            import { navigate, routeFor, routeUrl, usePath } from './lib/router';

            // One route per generated page: `/` is the entry form, `/<page-name>` opens any other page on its own (with mock data), `/pages` lists them all.
            // Forms the entry form opens through ShowDialog() are normally reached from it, but those whose constructor arguments
            // are not translated yet (see MIGRATION_REPORT.md) cannot be reached that way. Old `?page=<Name>` links are rewritten to routes.
            type PageProps = object;
            const loaders = import.meta.glob(['./pages/*.tsx', '!./pages/*.code.tsx']) as Record<string, () => Promise<Record<string, unknown>>>;
            const pages = Object.entries(loaders).map(([path, load]) => {
              const name = path.slice('./pages/'.length, -'.tsx'.length);
              return { name, route: routeFor(name), Page: lazy(async () => ({ default: (await load())[name] as ComponentType<PageProps> })) };
            });
            const legacy = new URLSearchParams(window.location.search).get('page');
            if (legacy !== null) navigate(pages.find((p) => p.name === legacy || p.name === legacy + 'Page')?.route ?? 'pages', true);

            // A page that throws while rendering shows its error instead of a blank screen.
            class Boundary extends Component<{ children: ReactNode }, { error: Error | null }> {
              state = { error: null as Error | null };
              static getDerivedStateFromError(error: Error) { return { error }; }
              render() { return this.state.error ? <pre role="alert">{String(this.state.error.stack ?? this.state.error)}</pre> : this.props.children; }
            }

            const goHome = () => navigate('');

            function Index() {
              return (
                <ul>
                  {pages.map((p) => <li key={p.name}><a href={routeUrl(p.route)} onClick={(e) => { e.preventDefault(); navigate(p.route); } }>{p.name}</a></li>)}
                </ul>
              );
            }

            function App() {
              const path = usePath();
              const selected = pages.find((p) => p.route === path);
              const view = path === '' ? <{{entryComponent}} /> : path === 'pages' ? <Index /> : selected ? <Suspense fallback={null}><selected.Page /></Suspense> : <Index />;
              return <Boundary key={path}>{view}</Boundary>;
            }

            // The entry form is the app shell; every ShowDialog() target opens through the dialog service.
            createRoot(document.getElementById('root')!).render(
              <StrictMode>
                <WfDialogProvider onRootClose={goHome}><App /></WfDialogProvider>
              </StrictMode>,
            );
            """, files);

        // Routes on the History API and the URL API, no router package: a route is a path relative to the document base.
        Put(appDir, "src/lib/router.ts", """
            import { useSyncExternalStore } from 'react';

            // The app base as a path ('/' at the site root, '/app/' under a sub-path), from the <base href> in index.html. Read once at start-up,
            // when the URL is the one the user opened, so later navigation cannot move it.
            const base = new URL(document.baseURI).pathname.replace(/\/?$/, '/');

            /** The route of the current URL relative to the base, without slashes ('' is the entry form). */
            export function currentRoute(): string {
              const path = decodeURIComponent(window.location.pathname);
              const rest = path.startsWith(base) ? path.slice(base.length) : path.replace(/^\//, '');
              return rest.replace(/\/+$/, '').replace(/^index\.html$/, '');
            }

            /** The URL (path on this origin) of a route, for links. */
            export function routeUrl(route: string): string {
              return base + encodeURI(route);
            }

            /** Opens a route without reloading the page (`replace` overwrites the current history entry). */
            export function navigate(route: string, replace = false): void {
              const url = routeUrl(route);
              if (replace) window.history.replaceState(null, '', url);
              else window.history.pushState(null, '', url);
              window.dispatchEvent(new window.Event('popstate'));
            }

            /** The route of a generated page: `NavigatorPage` -> `navigator`, `SearchResultsPage` -> `search-results`. */
            export function routeFor(componentName: string): string {
              return componentName.replace(/Page$/, '').replace(/([a-z0-9])([A-Z])/g, '$1-$2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1-$2').toLowerCase();
            }

            const subscribe = (listener: () => void) => {
              window.addEventListener('popstate', listener);
              return () => window.removeEventListener('popstate', listener);
            };

            /** The current route; the component re-renders when it changes (navigate(), back and forward). */
            export function usePath(): string {
              return useSyncExternalStore(subscribe, currentRoute);
            }
            """, files);

        // Runtime helpers for translated C# (string.Format with a non-literal format string).
        Put(appDir, "src/lib/csharp.ts", CsharpRuntime.Source, files);
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

    /// <summary>`<link rel="icon">` for the form icon (relative, so it resolves against the app base); empty when the entry form has no icon.</summary>
    private static string FaviconLink(string? favicon) => favicon is null ? "<!-- the entry form has no icon -->" : $"<link rel=\"icon\" href=\"{favicon}\" />";

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
