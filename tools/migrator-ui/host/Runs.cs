using System.Diagnostics;
using System.Net.Sockets;

namespace MigratorUi.Host;

/// <summary>One child process the host started (a migration, an npm install or a dev server) with its captured output.</summary>
public sealed class Run
{
    private readonly object _gate = new();
    private readonly List<string> _lines = new();
    private Process? _process;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string CommandLine { get; init; } = "";
    public string? WorkingDirectory { get; init; }
    public string? OutputDirectory { get; init; }
    public string? AppDirectory { get; init; }
    public DateTime Started { get; } = DateTime.UtcNow;
    public bool Done { get; private set; }
    public int? ExitCode { get; private set; }
    public bool Cancelled { get; private set; }
    public int? Port { get; set; }
    public bool Ready { get; set; }

    public string Url => Port is { } p ? $"http://localhost:{p}/" : "";

    public int LineCount { get { lock (_gate) return _lines.Count; } }

    public List<string> LinesFrom(int index)
    {
        lock (_gate) return index >= _lines.Count ? new List<string>() : _lines.GetRange(index, _lines.Count - index);
    }

    public void Add(string line)
    {
        lock (_gate) { _lines.Add(line); if (_lines.Count > 20000) _lines.RemoveRange(0, 5000); }
    }

    /// <summary>Starts the process; npm is a .cmd on Windows, so commands go through the shell there.</summary>
    public void Start(string fileName, IEnumerable<string> arguments, string? workingDirectory, Action? onExit = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments) info.ArgumentList.Add(a);
        info.Environment["NO_COLOR"] = "1";
        info.Environment["FORCE_COLOR"] = "0";
        info.Environment["CI"] = "1"; // no interactive prompts from npm / ng

        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) Add(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Add(e.Data); };
        _process.Exited += (_, _) =>
        {
            try { _process.WaitForExit(); ExitCode = _process.ExitCode; } catch (InvalidOperationException) { }
            Done = true;
            Ready = false;
            onExit?.Invoke();
        };
        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Add("Could not start the process: " + ex.Message);
            ExitCode = -1;
            Done = true;
        }
    }

    public void Stop()
    {
        if (Done || _process is null) return;
        Cancelled = true;
        var rootId = -1;
        try { rootId = _process.Id; _process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        if (OperatingSystem.IsWindows())
        {
            // The tree walk misses grandchildren whose parent already exited (npm hands over to node): kill by process id and by the port's listener too.
            if (rootId > 0) Taskkill(rootId);
            if (Port is { } port && ListenerPid(port) is { } listener) Taskkill(listener);
        }
        if (!_process.WaitForExit(3000)) { ExitCode ??= -1; }
        ExitCode ??= -1;
        Done = true;
        Ready = false;
    }

    private static void Taskkill(int pid)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("taskkill", $"/PID {pid} /T /F") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            p?.WaitForExit(5000);
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>The process listening on a TCP port (from `netstat -ano`), Windows only.</summary>
    private static int? ListenerPid(int port)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netstat", "-ano -p tcp") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            var text = p!.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            foreach (var line in text.Split((char)10))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && parts[3] == "LISTENING" && parts[1].EndsWith(":" + port, StringComparison.Ordinal) && int.TryParse(parts[4], out var pid)) return pid;
            }
        }
        catch (System.ComponentModel.Win32Exception) { }
        return null;
    }

    /// <summary>The command through the shell on Windows (`npm` and `npx` are .cmd files), directly elsewhere.</summary>
    public static (string File, List<string> Args) Shell(string command, params string[] args)
    {
        if (!OperatingSystem.IsWindows()) return (command, args.ToList());
        return ("cmd.exe", new List<string> { "/c", command }.Concat(args).ToList());
    }

    public static bool PortFree(int port)
    {
        try
        {
            using var client = new TcpClient();
            return !client.ConnectAsync("127.0.0.1", port).Wait(250);
        }
        catch (Exception) { return true; }
    }

    public static async Task<bool> WaitForPort(int port, TimeSpan timeout, Func<bool> aborted)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until && !aborted())
        {
            if (!PortFree(port)) return true;
            await Task.Delay(500);
        }
        return false;
    }
}
