namespace Migrator.Target.Core.Translation;

/// <summary>
/// `src/lib/api.ts` of an app generated with `--api`: the fetch client of the generated dotnet-api. Framework-free TypeScript.
/// An object's data travels with every call (the server restores it, runs the method and returns the changed data), so stateful data classes keep working.
/// </summary>
public static class ApiClientRuntime
{
    public const string Source = """
            // Client of the generated .NET API (dotnet-api). Base URL: window.__API_BASE__ (set it in index.html) or http://localhost:5000.
            export interface ApiMessage { text: string; caption: string; icon: string }

            type MessageHandler = (messages: ApiMessage[]) => void;
            let handler: MessageHandler = (messages) => { for (const m of messages) window.alert(m.text); };

            const camel = (name: string): string => name.charAt(0).toLowerCase() + name.slice(1);
            const base = (): string => ((globalThis as { __API_BASE__?: string }).__API_BASE__ ?? 'http://localhost:5000').replace(/\/+$/, '');

            interface Reply { result?: unknown; state?: Record<string, unknown>; messages?: ApiMessage[]; error?: string }

            async function post(cls: string, route: string, body: { state?: Record<string, unknown>; args: unknown[] }): Promise<Reply> {
              const response = await fetch(`${base()}/api/${cls}/${route}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
              let reply: Reply;
              try { reply = (await response.json()) as Reply; } catch { reply = { error: `${response.status} ${response.statusText}` }; }
              // The messages the original data class would have shown (MessageBox.Show) arrive with the reply, success or not.
              if (reply.messages && reply.messages.length > 0) handler(reply.messages);
              if (!response.ok || reply.error) throw new Error(reply.error ?? `${response.status} ${response.statusText}`);
              return reply;
            }

            export const api = {
              /** Receives the messages a server call collected; the app replaces the default (window.alert) with its dialog service. Returns an unsubscribe function. */
              onMessages(next: MessageHandler): () => void {
                handler = next;
                return () => { handler = (messages) => { for (const m of messages) window.alert(m.text); }; };
              },

              /** An instance method: `self`'s data members (`keys`) go to the server and the changed values come back into `self`. */
              async call<T>(cls: string, route: string, self: object, args: unknown[], keys: string[]): Promise<T> {
                const state: Record<string, unknown> = {};
                const data = self as Record<string, unknown>;
                for (const key of keys) state[camel(key)] = data[key];
                const reply = await post(cls, route, { state, args });
                for (const key of keys) if (reply.state && camel(key) in reply.state) data[key] = reply.state[camel(key)];
                return reply.result as T;
              },

              /** A static method: no object data. */
              async callStatic<T>(cls: string, route: string, args: unknown[]): Promise<T> {
                return (await post(cls, route, { args })).result as T;
              },
            };
            """;
}
