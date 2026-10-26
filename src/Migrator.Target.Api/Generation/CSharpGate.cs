using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Migrator.Target.Api.Generation;

public sealed record CompilerDiagnostic(string File, int Line, string Code, string Message);

/// <summary>`dotnet build` of the generated API: the oracle of the port, the way `tsc` is for the browser app.</summary>
internal static partial class CSharpGate
{
    /// <summary>The errors of the build (empty = clean), or null when the build could not be run at all (no SDK, restore failed without a readable error).</summary>
    public static List<CompilerDiagnostic>? Build(string projectDirectory, out string log)
    {
        log = "";
        try
        {
            var info = new ProcessStartInfo("dotnet", "build -c Release --nologo -v q -clp:NoSummary")
            {
                WorkingDirectory = projectDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            info.Environment["DOTNET_NOLOGO"] = "1";
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(6))) { try { process.Kill(true); } catch (InvalidOperationException) { } log = "dotnet build timed out"; return null; }
            log = stdout.Result + stderr.Result;

            var errors = new List<CompilerDiagnostic>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in ErrorRegex().Matches(log))
            {
                var d = new CompilerDiagnostic(m.Groups[1].Value.Trim().Replace('\\', '/'), int.Parse(m.Groups[2].Value), m.Groups[4].Value, m.Groups[5].Value.Trim());
                if (seen.Add($"{d.File}|{d.Line}|{d.Code}")) errors.Add(d);
            }
            // A non-zero exit without a parsable error (restore failure, missing SDK) is "could not run", never a false zero.
            if (errors.Count == 0 && process.ExitCode != 0) return null;
            return errors;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log = ex.Message;
            return null;
        }
    }

    [GeneratedRegex(@"^(.+?)\((\d+),(\d+)\): error (CS\d+): (.+?)(?: \[.*\])?\s*$", RegexOptions.Multiline)]
    private static partial Regex ErrorRegex();
}
