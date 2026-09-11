using System.Net.NetworkInformation;

namespace PropFlow.Deploy;

internal sealed record PreflightProblem(string What, string Fix);

/// <summary>
/// Everything that must be true before the first byte is written. Checked up front and reported
/// as one list, because discovering a missing Node three minutes into a database migration is a
/// worse experience than being told immediately.
/// </summary>
internal static class Preflight
{
    public const int DatabasePort = 5432;
    public const int ApiPort = 5001;
    public const int WebPort = 3000;

    public static async Task<IReadOnlyList<PreflightProblem>> RunAsync(
        DeploymentLayout layout, bool requirePortsFree, CancellationToken cancellationToken)
    {
        var problems = new List<PreflightProblem>();

        foreach (var missing in layout.MissingComponents())
            problems.Add(new PreflightProblem(
                $"The bundle is incomplete — missing {missing}.",
                "Re-download the release bundle; do not run the deployer outside the directory it shipped in."));

        var docker = ProcessRunner.Locate("docker");
        if (docker is null)
            problems.Add(new PreflightProblem(
                "Docker is not on PATH.",
                "Install Docker Desktop and start it, then open a new terminal."));
        else
        {
            var info = await ProcessRunner.RunAsync(docker, ["info", "--format", "{{.ServerVersion}}"],
                layout.Root, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!info.Succeeded)
                problems.Add(new PreflightProblem(
                    "Docker is installed but the daemon is not responding.",
                    "Start Docker Desktop and wait for it to report running, then try again."));
        }

        var node = ProcessRunner.Locate("node");
        if (node is null)
            problems.Add(new PreflightProblem(
                "Node.js is not on PATH.",
                "Install Node.js 22 (the web app is a Next.js server and needs a Node runtime), then open a new terminal."));
        else
        {
            var version = await ProcessRunner.RunAsync(node, ["--version"], layout.Root,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var major = ParseMajor(version.StandardOutput);
            if (major is not null && major < 20)
                problems.Add(new PreflightProblem(
                    $"Node.js {version.StandardOutput.Trim()} is too old for Next.js 16.",
                    "Install Node.js 22."));
        }

        if (ProcessRunner.Locate("npm") is null)
            problems.Add(new PreflightProblem(
                "npm is not on PATH.",
                "It ships with Node.js — reinstall Node.js 22."));

        if (requirePortsFree)
            foreach (var (port, used) in new[]
                     {
                         (DatabasePort, "PostgreSQL"),
                         (ApiPort, "the API"),
                         (WebPort, "the web app"),
                     })
                if (IsPortInUse(port))
                    problems.Add(new PreflightProblem(
                        $"Port {port} is already in use, and {used} needs it.",
                        port == DatabasePort
                            ? "A local PostgreSQL (for example `brew services list` showing postgresql started) is the usual cause. Stop it, or run `propflow-deploy down` if a previous deployment is still up."
                            : "Stop whatever is listening, or run `propflow-deploy down` if a previous deployment is still up."));

        return problems;
    }

    private static int? ParseMajor(string version)
    {
        var trimmed = version.Trim().TrimStart('v');
        var firstDot = trimmed.IndexOf('.');
        var head = firstDot < 0 ? trimmed : trimmed[..firstDot];
        return int.TryParse(head, out var major) ? major : null;
    }

    public static bool IsPortInUse(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == port);
        }
        catch (NetworkInformationException)
        {
            // If the platform will not tell us, do not invent a blocker.
            return false;
        }
    }
}
