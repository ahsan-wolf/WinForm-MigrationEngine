using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Migrator.Target.Core.Translation;

public sealed record TscDiagnostic(string File, int Line, int Column, string Code, string Message);

/// <summary>
/// The TypeScript compiler as an oracle for translated code: anything invented or mistranslated (a helper that does not exist, a wrong
/// type) fails `tsc --noEmit`, and the generator demotes exactly those methods back to commented C#. Runs `node typescript/bin/tsc`
/// directly (no shell), so paths with `&amp;` or spaces are safe.
/// </summary>
public static partial class CompileGate
{
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\): error (?<code>TS\d+): (?<msg>.*)$", RegexOptions.Multiline)]
    private static partial Regex DiagnosticRegex();

    public static string TscPath(string appDir) => Path.Combine(appDir, "node_modules", "typescript", "bin", "tsc");

    public static bool CanRun(string appDir) => File.Exists(TscPath(appDir));

    /// <summary>`npm install` in the generated app (explicit opt-in: needs network access and takes a while).</summary>
    public static bool TryInstall(string appDir, out string error)
    {
        error = "";
        var windows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo(windows ? "cmd.exe" : "npm")
        {
            WorkingDirectory = appDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (windows) { psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("npm"); }
        foreach (var a in new[] { "install", "--no-audit", "--no-fund" }) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi)!;
            var stderr = proc.StandardError.ReadToEndAsync();
            proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(TimeSpan.FromMinutes(10))) { proc.Kill(true); error = "npm install timed out"; return false; }
            if (proc.ExitCode != 0) { error = stderr.Result.Trim(); return false; }
            return CanRun(appDir);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Null when tsc cannot run (dependencies not installed or node missing); otherwise every error it reports.</summary>
    public static List<TscDiagnostic>? RunTsc(string appDir)
    {
        if (!CanRun(appDir)) return null;

        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = appDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in new[] { TscPath(appDir), "--noEmit", "--pretty", "false" }) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEndAsync();
            proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(TimeSpan.FromMinutes(5))) { proc.Kill(true); return null; }
            return DiagnosticRegex().Matches(stdout.Result).Select(m => new TscDiagnostic(
                m.Groups["file"].Value.Replace('\\', '/'), int.Parse(m.Groups["line"].Value), int.Parse(m.Groups["col"].Value),
                m.Groups["code"].Value, m.Groups["msg"].Value.Trim())).ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
