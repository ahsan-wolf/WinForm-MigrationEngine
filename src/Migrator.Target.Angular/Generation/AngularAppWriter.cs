using System.Text;
using Migrator.Target.Core.Generation;
using System.Text.Json;
using Migrator.Target.Core.Ir;

namespace Migrator.Target.Angular.Generation;

/// <summary>A generated page as the app skeleton needs to know it: its component class, its file base (`src/pages/{FileBase}.page.ts`) and its route.</summary>
internal sealed record PageEntry(string Component, string FileBase);

/// <summary>Deterministic files around the generated pages: package, Angular workspace config, entry point, routes, base styles. No per-form logic.</summary>
internal static class AngularAppWriter
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static void Write(string appDir, string appName, string entryComponent, string uiAngularDir, IReadOnlyList<PageEntry> pages, List<string> files, string? favicon, IReadOnlyList<CatalogEntry> catalog, bool api)
    {
        var uiAngular = Rel(appDir, uiAngularDir);
        var uiCoreDir = Path.GetFullPath(Path.Combine(uiAngularDir, "..", "ui-core"));
        var uiCore = Rel(appDir, uiCoreDir);

        var package = new Dictionary<string, object>
        {
            ["name"] = appName,
            ["private"] = true,
            ["version"] = "0.0.0",
            ["type"] = "module",
            ["scripts"] = new Dictionary<string, string>
            {
                ["start"] = "ng serve", ["build"] = "ng build", ["check"] = "ngc -p tsconfig.app.json --noEmit", ["lint"] = "eslint ."
            },
            ["dependencies"] = new Dictionary<string, string>
            {
                ["@angular/common"] = "^21.2.0", ["@angular/compiler"] = "^21.2.0", ["@angular/core"] = "^21.2.0", ["@angular/platform-browser"] = "^21.2.0",
                ["@angular/router"] = "^21.2.0", ["rxjs"] = "~7.8.0", ["zone.js"] = "~0.15.0"
            },
            ["devDependencies"] = new Dictionary<string, string>
            {
                ["@angular/build"] = "^21.2.0", ["@angular/cli"] = "^21.2.0", ["@angular/compiler-cli"] = "^21.2.0", ["@eslint/js"] = "^10.0.1", ["eslint"] = "^10.12.0",
                ["globals"] = "^17.13.0", ["typescript"] = "~5.9.3", ["typescript-eslint"] = "^8.71.1"
            }
        };
        OutputFiles.Put(appDir, "package.json", JsonSerializer.Serialize(package, Pretty) + "\n", files);

        OutputFiles.Put(appDir, "angular.json", """
            {
              "$schema": "./node_modules/@angular/cli/lib/config/schema.json",
              "version": 1,
              "cli": { "analytics": false },
              "newProjectRoot": "projects",
              "projects": {
                "app": {
                  "projectType": "application",
                  "root": "",
                  "sourceRoot": "src",
                  "architect": {
                    "build": {
                      "builder": "@angular/build:application",
                      "options": {
                        "browser": "src/main.ts",
                        "tsConfig": "tsconfig.app.json",
                        "index": "src/index.html",
                        "polyfills": ["zone.js"],
                        "assets": [{ "glob": "**/*", "input": "public" }],
                        "styles": ["src/wf.css", "src/app.css"],
                        "outputPath": "dist"
                      },
                      "configurations": { "development": { "optimization": false, "sourceMap": true } },
                      "defaultConfiguration": "development"
                    },
                    "serve": {
                      "builder": "@angular/build:dev-server",
                      "options": { "buildTarget": "app:build", "prebundle": false }
                    }
                  }
                }
              }
            }
            """, files);

        // The library is consumed as TypeScript source through path mappings (one copy of Angular: the app's), so no packaging step sits between them.
        OutputFiles.Put(appDir, "tsconfig.json", $$"""
            {
              "compileOnSave": false,
              "compilerOptions": {
                "strict": true,
                "noImplicitOverride": true,
                "skipLibCheck": true,
                "isolatedModules": true,
                "experimentalDecorators": true,
                "target": "ES2022",
                "module": "preserve",
                "moduleResolution": "bundler",
                "lib": ["ES2022", "dom"],
                "baseUrl": ".",
                "paths": {
                  "@angular/*": ["node_modules/@angular/*"],
                  "rxjs": ["node_modules/rxjs"],
                  "rxjs/*": ["node_modules/rxjs/*"],
                  "@wfm/ui-angular": ["{{uiAngular}}/src/public-api.ts"],
                  "@wfm/ui-core": ["{{uiCore}}/src/index.ts"]
                }
              },
              "angularCompilerOptions": { "strictTemplates": true, "strictInjectionParameters": true }
            }
            """, files);

        OutputFiles.Put(appDir, "tsconfig.app.json", $$"""
            {
              "extends": "./tsconfig.json",
              "compilerOptions": { "outDir": "./out-tsc/app", "types": [] },
              "files": ["src/main.ts"],
              "include": ["src/**/*.d.ts", "{{uiAngular}}/src/**/*.ts", "{{uiCore}}/src/**/*.ts"]
            }
            """, files);

        OutputFiles.Put(appDir, "eslint.config.js", """
            import js from '@eslint/js';
            import globals from 'globals';
            import tseslint from 'typescript-eslint';

            export default tseslint.config(
              { ignores: ['dist', '.angular', 'out-tsc'] },
              {
                files: ['**/*.ts'],
                extends: [js.configs.recommended, ...tseslint.configs.recommended],
                languageOptions: { globals: globals.browser },
                rules: {
                  '@typescript-eslint/no-unused-vars': ['error', { args: 'none', argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_' }],
                  // Stylistic rules that translated C# trips on without any bug.
                  'no-extra-boolean-cast': 'off',
                  'no-useless-assignment': 'off',
                  'no-unassigned-vars': 'off',
                  'no-useless-catch': 'off',
                  'no-constant-condition': 'off',
                  'preserve-caught-error': 'off',
                },
              },
            );
            """, files);

        OutputFiles.Put(appDir, "src/index.html", $$"""
            <!doctype html>
            <html lang="en">
              <head>
                <meta charset="utf-8" />
                <title>{{appName}}</title>
                <base href="/" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                {{(favicon is null ? "<!-- the entry form has no icon -->" : $"<link rel=\"icon\" href=\"{favicon}\" />")}}
              </head>
              <body>
                <app-root></app-root>
              </body>
            </html>
            """, files);

        // The shared stylesheet imports the design tokens by a relative path; the app gets one self-contained file.
        var wf = Path.Combine(uiCoreDir, "src", "wf.css");
        var tokens = Path.Combine(uiAngularDir, "..", "ui-spec", "tokens.css");
        var wfCss = File.Exists(wf) ? File.ReadAllText(wf) : "/* @wfm/ui-core wf.css was not found next to the library */";
        wfCss = System.Text.RegularExpressions.Regex.Replace(wfCss, @"@import\s+['""][^'""]*tokens\.css['""];", _ => File.Exists(tokens) ? File.ReadAllText(tokens) : "");
        OutputFiles.Put(appDir, "src/wf.css", wfCss, files);
        OutputFiles.Put(appDir, "src/app.css", """
            html, body { height: 100%; margin: 0; }
            app-root { display: block; height: 100%; }
            .wfm-body { display: flex; flex-direction: column; gap: 8px; padding: 8px; flex: 1 1 auto; min-height: 0; overflow: auto; }
            .wfm-row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
            .wfm-main { display: flex; gap: 8px; flex: 1 1 auto; min-height: 0; }
            .wfm-fill { flex: 1 1 auto; min-width: 0; min-height: 0; display: flex; flex-direction: column; }
            """, files);

        OutputFiles.Put(appDir, "src/main.ts", """
            import { bootstrapApplication } from '@angular/platform-browser';
            import { appConfig } from './app/app.config';
            import { AppComponent } from './app/app.component';

            bootstrapApplication(AppComponent, appConfig).catch((error) => console.error(error));
            """, files);

        OutputFiles.Put(appDir, "src/app/app.config.ts", """
            import { type ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
            import { provideRouter } from '@angular/router';
            import { routes } from './app.routes';

            // zone.js re-renders after every handler, so the page model can be a plain object that handlers write to.
            export const appConfig: ApplicationConfig = {
              providers: [provideZoneChangeDetection({ eventCoalescing: true }), provideRouter(routes)],
            };
            """, files);

        // One route per generated page, like the React app: '' is the entry form, '/<page-name>' opens a page on its own, '/pages' lists them.
        var routes = new StringBuilder();
        routes.AppendLine("import type { Routes } from '@angular/router';");
        routes.AppendLine("import { PagesComponent } from './pages.component';");
        if (catalog.Count > 0) routes.AppendLine("import { ModulesComponent } from './modules.component';");
        routes.AppendLine();
        routes.AppendLine("export const routes: Routes = [");
        routes.AppendLine($"  {{ path: '', pathMatch: 'full', loadComponent: () => import('../pages/{pages.FirstOrDefault(p => p.Component == entryComponent)?.FileBase ?? "entry"}.page').then((m) => m.{entryComponent}) }},");
        routes.AppendLine("  { path: 'pages', component: PagesComponent },");
        if (catalog.Count > 0) routes.AppendLine("  { path: 'modules', component: ModulesComponent },");
        foreach (var page in pages.OrderBy(p => p.FileBase, StringComparer.Ordinal))
            routes.AppendLine($"  {{ path: '{page.FileBase}', loadComponent: () => import('../pages/{page.FileBase}.page').then((m) => m.{page.Component}) }},");
        routes.AppendLine("  { path: '**', component: PagesComponent },");
        routes.AppendLine("];");
        OutputFiles.Put(appDir, "src/app/app.routes.ts", routes.ToString(), files);

        var modulesLink = catalog.Count > 0 ? "<li><a routerLink=\"/modules\"><b>Modules</b> (units the original opens dynamically)</a></li>" : "";
        if (catalog.Count > 0) OutputFiles.Put(appDir, "src/app/modules.component.ts", ModulesSource(catalog, pages), files);
        var list = string.Join(", ", pages.OrderBy(p => p.FileBase, StringComparer.Ordinal).Select(p => $"{{ name: '{p.Component}', route: '/{p.FileBase}' }}"));
        OutputFiles.Put(appDir, "src/app/pages.component.ts", $$$"""
            import { Component } from '@angular/core';
            import { RouterLink } from '@angular/router';

            /** The list of every generated page (`/pages`); an unknown path shows it too. */
            @Component({
              selector: 'app-pages',
              standalone: true,
              imports: [RouterLink],
              template: `<ul>{{{modulesLink}}}@for (page of pages; track page.route) { <li><a [routerLink]="page.route">{{ page.name }}</a></li> }</ul>`,
            })
            export class PagesComponent {
              protected readonly pages = [{{{list}}}];
            }
            """, files);

        OutputFiles.Put(appDir, "src/app/app.component.ts", $$"""
            import { Component, inject } from '@angular/core';
            import { Router, RouterOutlet } from '@angular/router';
            import { WfDialogService } from '@wfm/ui-angular';{{ApiImport(api)}}

            @Component({
              selector: 'app-root',
              standalone: true,
              imports: [RouterOutlet],
              template: `<router-outlet />`,
            })
            export class AppComponent {
              constructor() {
                // dialog.close() on a page that is not in a dialog leaves it: back to the entry form.
                const router = inject(Router);
                const dialog = inject(WfDialogService);
                dialog.onRootClose = () => { void router.navigateByUrl('/'); };{{ApiHook(api)}}
              }
            }
            """, files);

        OutputFiles.Put(appDir, "src/lib/csharp.ts", Migrator.Target.Core.Translation.CsharpRuntime.Source, files);
    }

    /// <summary>
    /// The `/modules` page: the units the original loads dynamically (or by permission logic), listed by archetype; the selected one opens in the content area.
    /// </summary>
    private static string ModulesSource(IReadOnlyList<CatalogEntry> catalog, IReadOnlyList<PageEntry> pages)
    {
        var entries = string.Join(",\n  ", catalog.Select(e =>
        {
            var file = pages.FirstOrDefault(p => p.Component == e.Component)?.FileBase ?? "";
            return $"{{ unit: {System.Text.Json.JsonSerializer.Serialize(e.Unit)}, archetype: {System.Text.Json.JsonSerializer.Serialize(e.Archetype.ToString())}, load: () => import('../pages/{file}.page').then((m) => m.{e.Component}) }}";
        }));
        return $$$"""
            import { Component, signal, type Type } from '@angular/core';
            import { NgComponentOutlet } from '@angular/common';

            interface Module { unit: string; archetype: string; load: () => Promise<Type<unknown>> }

            // The units the original opens dynamically (a menu tree, a permission check, a database row): every one the entry form never opens by code,
            // grouped by what it is (grid, tree, menu, toolbar, list or form). Selecting one opens it here, standing in for the original loading logic.
            const modules: Module[] = [
              {{{entries}}},
            ];

            @Component({
              selector: 'app-modules',
              standalone: true,
              imports: [NgComponentOutlet],
              template: `<div class="wfm-modules" style="display: flex; height: 100%; gap: 8px">
                <nav aria-label="Modules" style="width: 280px; overflow: auto; flex: 0 0 auto; padding: 8px">
                  @for (a of archetypes; track a) {
                    <section>
                      <h3>{{ a }} ({{ of(a).length }})</h3>
                      <ul style="list-style: none; margin: 0; padding: 0">
                        @for (m of of(a); track m.unit) {
                          <li><button type="button" style="all: unset; cursor: pointer" [style.font-weight]="selected()?.unit === m.unit ? 700 : 400" (click)="open(m)">{{ m.unit }}</button></li>
                        }
                      </ul>
                    </section>
                  }
                </nav>
                <main style="flex: 1 1 auto; min-width: 0; overflow: auto; padding: 8px">
                  @if (view(); as v) { <ng-container *ngComponentOutlet="v" /> } @else { <p>Select a module.</p> }
                </main>
              </div>`,
            })
            export class ModulesComponent {
              protected readonly archetypes = [...new Set(modules.map((m) => m.archetype))];
              protected readonly selected = signal<Module | null>(null);
              protected readonly view = signal<Type<unknown> | null>(null);
              protected of(archetype: string): Module[] { return modules.filter((m) => m.archetype === archetype); }
              protected async open(m: Module): Promise<void> {
                this.view.set(null);
                this.selected.set(m);
                this.view.set(await m.load());
              }
            }
            """;
    }

    private static string ApiImport(bool on) => on ? "\nimport { api } from '../lib/api';" : "";
    private static string ApiHook(bool on) => on
        ? "\n    // Messages the server collected from the original data classes (MessageBox.Show) open as popups.\n    api.onMessages((messages) => { for (const m of messages) void dialog.alert(m.text, m.caption || undefined); });"
        : "";

    private static string Rel(string fromDir, string toDir) => Path.GetRelativePath(fromDir, toDir).Replace('\\', '/');
}
