namespace Migrator.Target.Core.Translation;

/// <summary>The runtime helpers translated C# calls (formatString, tryParseInt, Path.* ...). Written as <c>lib/csharp.ts</c> by every target; framework-free TypeScript.</summary>
public static class CsharpRuntime
{
    /// <summary>Names of the helpers; a page imports those its code uses.</summary>
    public static readonly string[] Helpers = { "formatString", "tryParseInt", "tryParseNumber", "pathCombine", "getFileName", "getFileNameWithoutExtension", "getDirectoryName", "getExtension" };

    public const string Source = """
            // Helpers used by code translated from C#. Format specifiers ({0:N2}) are not interpreted: values are converted with String().
            export function formatString(format: string, ...args: unknown[]): string {
              return format.replace(/\{(\d+)(?::[^}]*)?\}/g, (_match, index: string) => String(args[Number(index)] ?? ''));
            }

            /** int.TryParse: the integer a string holds (digits with an optional sign), or null when it is not one. */
            export function tryParseInt(text: string): number | null {
              return /^\s*[+-]?\d+\s*$/.test(text) ? Number(text) : null;
            }

            /** double.TryParse / decimal.TryParse (invariant format), or null. */
            export function tryParseNumber(text: string): number | null {
              return /^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?\s*$/.test(text) ? Number(text) : null;
            }

            // System.IO.Path on plain strings (no file access): both '/' and '\\' separate, so Windows paths keep working in the browser.
            const separators = /[\\/]/;
            const rooted = (p: string) => /^([\\/]|[A-Za-z]:)/.test(p);

            /** Path.Combine: a later rooted part replaces what came before; the separator of the first part is reused ('\\' for Windows-looking paths). */
            export function pathCombine(...parts: string[]): string {
              let result = '';
              for (const part of parts) {
                if (part === '') continue;
                if (result === '' || rooted(part)) { result = part; continue; }
                const sep = result.includes('\\') || /^[A-Za-z]:/.test(result) ? '\\' : '/';
                result = separators.test(result[result.length - 1]) ? result + part : result + sep + part;
              }
              return result;
            }

            /** Path.GetFileName */
            export function getFileName(path: string): string {
              return path.slice(path.search(/[^\\/]*$/));
            }

            /** Path.GetExtension: the last '.' of the file name onwards, '' when there is none. */
            export function getExtension(path: string): string {
              const name = getFileName(path);
              const dot = name.lastIndexOf('.');
              return dot > 0 && dot < name.length - 1 ? name.slice(dot) : '';
            }

            /** Path.GetFileNameWithoutExtension */
            export function getFileNameWithoutExtension(path: string): string {
              const name = getFileName(path);
              const ext = getExtension(name);
              return ext ? name.slice(0, name.length - ext.length) : name;
            }

            /** Path.GetDirectoryName: everything before the last separator ('' for a bare file name). */
            export function getDirectoryName(path: string): string {
              const cut = path.search(/[\\/][^\\/]*$/);
              return cut <= 0 ? (cut === 0 ? path.slice(0, 1) : '') : path.slice(0, cut);
            }
            """;
}
