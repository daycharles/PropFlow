using System.Diagnostics;
using System.Text;

namespace PropFlow.Deploy;

internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>Whichever stream carried the message, trimmed — for one-line error reporting.</summary>
    public string Message =>
        (string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError).Trim();
}

internal static class ProcessRunner
{
    /// <summary>
    /// Runs a command to completion and captures both streams.
    /// </summary>
    public static async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = StartInfo(fileName, arguments, workingDirectory, environment) };
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };

        Start(process, fileName);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new CommandResult(process.ExitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Starts a long-running process with both streams redirected to <paramref name="logFile"/>,
    /// and returns it still running. The caller owns its lifetime.
    ///
    /// The log is opened before the process starts, so an absent log file always means the
    /// process never launched — never "it ran cleanly and said nothing".
    /// </summary>
    public static Process StartBackground(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string logFile,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        var writer = new StreamWriter(new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };

        var process = new Process { StartInfo = StartInfo(fileName, arguments, workingDirectory, environment) };
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) writer.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) writer.WriteLine(e.Data); };
        process.Exited += (_, _) => writer.Dispose();
        process.EnableRaisingEvents = true;

        Start(process, fileName);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static ProcessStartInfo StartInfo(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (key, value) in environment)
                info.Environment[key] = value;
        return info;
    }

    private static void Start(Process process, string fileName)
    {
        try
        {
            process.Start();
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"Could not start '{fileName}'. Is it installed and on PATH? ({error.Message})", error);
        }
    }

    /// <summary>
    /// Resolves an executable the way a shell would. Windows needs the extension, and npm/node
    /// ship as <c>.cmd</c> shims there, which <see cref="Process"/> will not launch without it.
    /// </summary>
    public static string? Locate(string command)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { command + ".exe", command + ".cmd", command + ".bat", command }
            : [command];
        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in candidates)
            {
                string full;
                try
                {
                    full = Path.Combine(directory.Trim('"'), candidate);
                }
                catch (ArgumentException)
                {
                    continue; // A malformed PATH entry is not fatal.
                }
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }
}
