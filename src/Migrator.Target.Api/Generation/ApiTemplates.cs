namespace Migrator.Target.Api.Generation;

/// <summary>Fixed files of the generated API project (no per-application content; tokens in `__DOUBLE_UNDERSCORES__` are filled by the generator).</summary>
internal static class ApiTemplates
{
    public const string Project = """
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <AssemblyName>GeneratedApi</AssemblyName>
            <RootNamespace>Generated.Api</RootNamespace>
            <!-- Ported code is the original, decompiled-era C#: nullable annotations and unused-variable warnings are not meaningful. -->
            <Nullable>disable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
            <NoWarn>$(NoWarn);CS0168;CS0169;CS0219;CS0649;CS0414;CS1998;CS0162;CS0105;CS8632;CS0108;CS0114;SYSLIB0050</NoWarn>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="__PROVIDER_PACKAGE__" Version="__PROVIDER_VERSION__" />
          </ItemGroup>

        </Project>
        """;

    public const string AppSettings = """
        {
          "Logging": { "LogLevel": { "Default": "Warning", "Microsoft.AspNetCore": "Warning" } },
          "Cors": { "Origins": [ "http://localhost:5173", "http://localhost:4200" ] },
          "AppSettings": {
            "_note": "Values the original read from App.config appSettings. Set them with environment variables (AppSettings__key) or user-secrets; secrets never belong in this file."
          }
        }
        """;

    public const string Shims = """
        // Stand-ins for the Windows Forms and System.Configuration members the original data classes call, so the original logic compiles unchanged on the server.
        namespace Generated.Api;

        public enum DialogResult { None, OK, Cancel, Abort, Retry, Ignore, Yes, No }
        public enum MessageBoxButtons { OK, OKCancel, AbortRetryIgnore, YesNoCancel, YesNo, RetryCancel }
        public enum MessageBoxIcon { None = 0, Hand = 16, Error = 16, Stop = 16, Question = 32, Exclamation = 48, Warning = 48, Asterisk = 64, Information = 64 }
        public enum MessageBoxDefaultButton { Button1 = 0, Button2 = 256, Button3 = 512 }

        /// <summary>`MessageBox.Show(...)` in a data class: the text is collected and returned to the browser (shown there as a popup); the call answers like a user who accepted.</summary>
        public static class MessageBox
        {
            public static DialogResult Show(string text) => Record(text, "", MessageBoxButtons.OK, MessageBoxIcon.None);
            public static DialogResult Show(string text, string caption) => Record(text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
            public static DialogResult Show(string text, string caption, MessageBoxButtons buttons) => Record(text, caption, buttons, MessageBoxIcon.None);
            public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Record(text, caption, buttons, icon);
            public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) => Record(text, caption, buttons, icon);
            public static DialogResult Show(object owner, string text) => Record(text, "", MessageBoxButtons.OK, MessageBoxIcon.None);
            public static DialogResult Show(object owner, string text, string caption) => Record(text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
            public static DialogResult Show(object owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Record(text, caption, buttons, icon);

            private static DialogResult Record(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            {
                ApiRuntime.AddMessage(text, caption, icon.ToString());
                return buttons switch
                {
                    MessageBoxButtons.YesNo or MessageBoxButtons.YesNoCancel => DialogResult.Yes,
                    MessageBoxButtons.RetryCancel => DialogResult.Retry,
                    MessageBoxButtons.AbortRetryIgnore => DialogResult.Ignore,
                    _ => DialogResult.OK
                };
            }
        }

        /// <summary>`ConfigurationManager.AppSettings["key"]`: read from configuration (`AppSettings:key`) or the environment (`AppSettings__key`), never from a generated file.</summary>
        public static class ConfigurationManager
        {
            public static SettingsBag AppSettings { get; } = new();
            public static ConnectionStringBag ConnectionStrings { get; } = new();
        }

        public sealed class SettingsBag
        {
            public string this[string key] => ApiRuntime.Setting(key);
            public string Get(string key) => ApiRuntime.Setting(key);
        }

        public sealed class ConnectionStringEntry { public string ConnectionString { get; init; } = ""; public string Name { get; init; } = ""; }

        public sealed class ConnectionStringBag
        {
            public ConnectionStringEntry this[string name] => new() { Name = name, ConnectionString = ApiRuntime.Setting("ConnectionStrings:" + name) };
        }
        """;

    public const string Runtime = """
        using System.Collections;
        using System.Data;
        using System.Text.Json;
        using System.Text.RegularExpressions;

        namespace Generated.Api;

        public sealed record ApiMessage(string Text, string Caption, string Icon);

        /// <summary>The body of every operation call: the object's data (`state`) and the call's arguments in order.</summary>
        public sealed record CallRequest(JsonElement? State, JsonElement[] Args);

        public static class ApiRuntime
        {
            private static IConfiguration _configuration = new ConfigurationBuilder().Build();
            private static readonly AsyncLocal<List<ApiMessage>> Messages = new();
            private static readonly string[] StripKeys = __STRIP_KEYS__;

            public static readonly JsonSerializerOptions Json = new()
            {
                IncludeFields = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DictionaryKeyPolicy = null,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            };

            public static void Configure(IConfiguration configuration) => _configuration = configuration;

            public static string Setting(string key)
            {
                var value = _configuration["AppSettings:" + key] ?? _configuration[key] ?? Environment.GetEnvironmentVariable("AppSettings__" + key.Replace(":", "__")) ?? "";
                return LooksLikeConnectionString(value) ? ForProvider(value) : value;
            }

            /// <summary>Values of settings the original kept: used to make sure none of them reaches a response.</summary>
            private static IEnumerable<string> KnownValues() =>
                _configuration.GetSection("AppSettings").GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrEmpty(v) && v.Length > 3).Cast<string>();

            private static bool LooksLikeConnectionString(string value) => value.Contains('=') && value.Contains(';');

            /// <summary>An OLE DB connection string carries keys the ADO.NET provider of the API does not know (`Provider=...`): they are removed.</summary>
            private static string ForProvider(string value)
            {
                foreach (var key in StripKeys)
                    value = Regex.Replace(value, @"(^|;)\s*" + Regex.Escape(key) + @"\s*=[^;]*", "$1", RegexOptions.IgnoreCase);
                return value.TrimStart(';');
            }

            public static void AddMessage(string text, string caption, string icon) => (Messages.Value ??= new List<ApiMessage>()).Add(new ApiMessage(text, caption, icon));

            private static List<ApiMessage> TakeMessages()
            {
                var list = Messages.Value ?? new List<ApiMessage>();
                Messages.Value = new List<ApiMessage>();
                return list;
            }

            public static string Redact(string text)
            {
                foreach (var secret in KnownValues()) text = text.Replace(secret, "***");
                return text;
            }

            public static T Arg<T>(JsonElement[] args, int index) =>
                index < args.Length && args[index].ValueKind != JsonValueKind.Null && args[index].ValueKind != JsonValueKind.Undefined
                    ? args[index].Deserialize<T>(Json)!
                    : default!;

            public static async Task<IResult> Run<T>(CallRequest call, Func<T, JsonElement[], Task<object>> body) where T : class
            {
                Messages.Value = new List<ApiMessage>();
                try
                {
                    var instance = call.State is { ValueKind: JsonValueKind.Object } state ? state.Deserialize<T>(Json) ?? Activator.CreateInstance<T>() : Activator.CreateInstance<T>();
                    var result = await body(instance, call.Args ?? Array.Empty<JsonElement>());
                    return Results.Json(new { result = Shape(result), state = instance, messages = TakeMessages() }, Json);
                }
                catch (Exception ex)
                {
                    return Fail(ex);
                }
            }

            public static async Task<IResult> RunStatic(CallRequest call, Func<JsonElement[], Task<object>> body)
            {
                Messages.Value = new List<ApiMessage>();
                try
                {
                    var result = await body(call.Args ?? Array.Empty<JsonElement>());
                    return Results.Json(new { result = Shape(result), messages = TakeMessages() }, Json);
                }
                catch (Exception ex)
                {
                    return Fail(ex);
                }
            }

            private static IResult Fail(Exception ex)
            {
                var message = Redact(ex.GetBaseException().Message);
                return Results.Json(new { error = message, messages = TakeMessages() }, Json, statusCode: 500);
            }

            /// <summary>What the browser receives: a DataSet becomes `{ table: rows[] }`, a DataTable or DataRow plain rows; everything else is serialized as it is.</summary>
            public static object Shape(object value) => value switch
            {
                null => null,
                DataSet set => set.Tables.Cast<DataTable>().ToDictionary(t => t.TableName, t => (object)Rows(t)),
                DataTable table => Rows(table),
                DataRow row => Row(row),
                DBNull => null,
                _ => value
            };

            private static List<Dictionary<string, object>> Rows(DataTable table) => table.Rows.Cast<DataRow>().Select(Row).ToList();

            private static Dictionary<string, object> Row(DataRow row)
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DataColumn c in row.Table.Columns) map[c.ColumnName] = row[c] is DBNull ? null : row[c];
                return map;
            }
        }
        """;

    public const string Program = """
        using Generated.Api;

        var builder = WebApplication.CreateBuilder(args);
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173", "http://localhost:4200" };
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();
        ApiRuntime.Configure(app.Configuration);
        app.UseCors();

        app.MapGet("/api/health", () => new { status = "ok" });
        Endpoints.Map(app);

        app.Run();
        """;
}
