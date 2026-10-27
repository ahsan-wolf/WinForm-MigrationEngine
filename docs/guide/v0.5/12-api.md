# 12. The generated .NET API (`dotnet-api`) and `--api`

A browser app cannot run data access (ADO.NET, DataSets, `MessageBox` in the data layer, `ConfigurationManager`). Before this chapter's feature such classes were **left out** of the generated app (`rules/server-bound.json`: a class where at least half of its methods use a data-access, file or configuration marker) and every call into them became a TODO. Now they can run **behind a generated .NET 10 REST API**, and the browser app calls it.

## 12.1 Two commands, one source
```bash
migrator migrate --source <src> --out <out> --target dotnet-api                       # the server: <out>/dotnet-api
migrator migrate --source <src> --out <out> --translate rules --compile-gate --api    # the app (react-app or angular-app) calls it
```
`--db-provider sqlclient|oledb` selects the ADO.NET provider of the API (default `sqlclient`: `OleDb*` types are aliased to `Microsoft.Data.SqlClient`; `oledb` keeps `System.Data.OleDb`, Windows only). Without `--api` the web output is unchanged.

## 12.2 What the API is
- **Operations.** Every public method of a server-bound class is `POST /api/<Class>/<Method>` (overloads: `<Method>-<parameterCount>`). Body `{ "state": { ...public data of the object... }, "args": [ ... ] }`, reply `{ "result", "state", "messages" }`; errors `{ "error", "messages" }` (HTTP 500). The **state** is what makes the old stateful data classes (public fields hold the record, `Save()` reads them) work: the server creates the object, restores the state, calls the method and returns the changed state.
- **The original logic, ported.** The class is re-emitted from `support.ir.json` as C# with the original bodies. `MessageBox`, `DialogResult` and `ConfigurationManager.AppSettings` compile against small shims (messages are collected and returned; settings come from configuration or `AppSettings__<key>` environment variables, never from a generated file); `OleDb*` types are aliased to the provider by `rules/api-port.json` (data: extend a copy per project). `DataSet` / `DataTable` results become JSON (`{ table: rows[] }`).
- **`dotnet build` is the gate.** A member that does not compile on .NET 10 (a form, a report viewer, a Windows-only call) is replaced by a `NotSupportedException` with the original kept as a comment and listed in `API_REPORT.md`.
- **Report** (`API_REPORT.md`): operations per class, demoted members, **SQL built by string concatenation** (injection risk: the port does not parameterize), Windows-only calls, Access-style SQL, and the configuration keys the code reads (secret-looking ones marked SERVER-ONLY).
- **Safety.** Connection strings and passwords are never written to generated files or responses (known setting values are redacted from error messages). CORS origins come from `Cors:Origins`. There is **no authentication**: run it behind your own and with a least-privilege database account.

## 12.3 What the app does with it
With `--api` the support module of the app contains the server-bound classes as TypeScript classes whose public methods are `async` calls (`src/lib/api.ts`, base URL `window.__API_BASE__` or `http://localhost:5000`): the object's data goes with the call and the changed data comes back into the object. Translated pages call them like the original (`const d = await ei.GetBPV(id)`); the async marking spreads to the callers. Messages the data class showed arrive with the reply and open as popups through the dialog service. Calls on **instances** of project classes (`Record r = new Record(); r.Compute();`) are translated now for every project, API or not.

## 12.4 Run it
```bash
cd <out>/dotnet-api
set AppSettings__<key>=<value>          # keys listed in API_REPORT.md (connection string and the like)
dotnet run --urls http://localhost:5000
```
The Migrator UI has the target `dotnet-api`, the *Call the generated API* option and Install (`dotnet build`) / Start (`dotnet run`) for the API folder.

## 12.5 Verification
A synthetic sample (a stateful data class with one pure method, one method using `SqlConnection` and `MessageBox`, a form calling both) in the browser: the object is computed through the API (`ACME: 7.5`), a database failure opens the server's message as a popup, no secret in any response. A second application (single-file forms, OleDb data layer): 23 server-bound classes, 119 operations, the API builds (2 members demoted), React and Angular apps with `--api` pass `tsc` / `ngc`, build and lint.

## 12.6 Limits
Parameterization of the ported (concatenated) SQL, authentication and authorization, `ref` / `out` operations and classes without a parameterless constructor are not part of this slice. The repository layer (12.7) is parameterized, but the ported classes do not call it yet.

## 12.7 Database structure and repositories (sqlclient provider)
The database itself is not part of the input, only the SQL text inside the code. The `dotnet-api` target recovers the structure from it and builds a repository layer on top.
- **Schema inference** (`Schema/SqlSchemaExtractor`): SQL is rebuilt from the C# string literals of each statement (`string.Concat`, `+`, `object[]` fragments), with every value kept as a placeholder. Tables come from FROM / JOIN / INSERT INTO / UPDATE / DELETE; columns from INSERT lists, SET lists, SELECT lists and WHERE / ON comparisons; a key from `select max(col) + 1 from T`; relations from join conditions. A column's type is the C# type of the value the code writes or compares (a field, a parameter, a local), else a name hint from `rules/schema-infer.json` (data; extend a copy per project), else `nvarchar(255)`. A table with no key evidence gets an identity `RowId`. Report work tables (emptied, then refilled by `INSERT ... SELECT`) are flagged.
- **Output**: `schema.sql` (idempotent T-SQL, batches separated by `GO`) and `SCHEMA_REPORT.md` (every table, the evidence behind each type, relations). No foreign keys are created; join columns get indexes.
- **Repositories** (`Data/`): per table an entity, `I<Table>Repository` and a repository on one shared `SqlRepositoryBase` (parameterized commands only; caller-supplied column names are checked against the table's column list). Endpoints: `GET|POST /api/data/<table>`, `GET|PUT|DELETE /api/data/<table>/<key>`, `GET /api/data/<table>/next-id` (for `max + 1` keys; an empty table gives 1), and `GET|DELETE ...?column=<name>&value=<v>`.
- **Database**: with no configuration the API uses SQL Server LocalDB (`(localdb)\MSSQLLocalDB`, Windows login, database `GeneratedApi`), creates the database and the tables on first start (`Database:AutoCreate`, default true for LocalDB only) and points the ported classes at the same database through their old connection `AppSettings` key. Any other server: `ConnectionStrings__Default` in the environment.
- **Limits**: types, keys and nullability are guesses; columns the code never names are missing; `select *` tables show only columns other statements mention. Compare with the real database before trusting it.
