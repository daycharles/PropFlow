using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PropFlow.Deploy;

/// <summary>
/// Brings the stack up and down in a <b>Production</b> posture.
///
/// Production is not merely a label here — the API refuses to start unless three things are
/// configured, and this class is what supplies them:
///
///   DataProtection:KeyPath          a persistent key ring, or auth and antiforgery cookies do
///                                   not survive a restart (Program.cs:58-62)
///   ForwardedHeaders:Enabled        plus an explicit KnownProxies allow-list
///                                   (DeploymentConfiguration.cs:40-43)
///   Attachments:RootPath            where attachment blobs live
///                                   (LocalAttachmentStorage.cs:52-54)
///
/// Two deliberate deviations from a real deployment, both called out in docs/DEPLOYMENT.md:
/// TLS is terminated by Kestrel with a locally-issued self-signed certificate rather than by an
/// ingress, and the forwarded-headers allow-list names loopback because there is no proxy in
/// front. Everything else — the restricted runtime role, forced RLS, HSTS, the production rate
/// limit, the persistent key ring — is the real thing.
/// </summary>
internal sealed class StackController(DeploymentLayout layout, Action<string> log)
{
    private const string ApiOrigin = "https://localhost:5001";
    private const string WebOrigin = "http://127.0.0.1:3000";

    public async Task<int> UpAsync(bool seedDemo, CancellationToken cancellationToken)
    {
        var problems = await Preflight.RunAsync(layout, requirePortsFree: true, cancellationToken).ConfigureAwait(false);
        if (problems.Count > 0) return ReportPreflight(problems);

        layout.EnsureStateDirectories();
        var secrets = DeploymentSecrets.LoadOrCreate(layout, log);
        CertificateFactory.EnsureCertificate(layout, secrets, log);

        log("");
        log("1/6  Starting PostgreSQL");
        if (!await ComposeAsync(secrets, ["up", "-d", "--wait"], cancellationToken).ConfigureAwait(false)) return 1;

        log("2/6  Applying migrations");
        if (!await AdminAsync("migrate", secrets, cancellationToken).ConfigureAwait(false)) return 1;

        log("3/6  Configuring the restricted runtime role");
        if (!await AdminAsync("configure-runtime", secrets, cancellationToken).ConfigureAwait(false)) return 1;

        if (seedDemo)
        {
            log("4/6  Seeding demo data");
            if (!await AdminAsync("seed-demo", secrets, cancellationToken).ConfigureAwait(false)) return 1;
        }
        else
        {
            log("4/6  Skipping demo data (--no-seed)");
        }

        log("5/6  Building the web app (first run only — this is the slow step)");
        if (!await BuildWebAsync(cancellationToken).ConfigureAwait(false)) return 1;

        log("6/6  Starting the API and the web app");
        var api = StartApi(secrets);
        var web = StartWeb();
        SaveProcessIds(api, web);

        if (!await WaitForApiAsync(secrets, cancellationToken).ConfigureAwait(false))
        {
            DumpLog(layout.ApiLog, "API");
            return 1;
        }
        if (!await WaitForWebAsync(cancellationToken).ConfigureAwait(false))
        {
            DumpLog(layout.WebLog, "web app");
            return 1;
        }

        log("");
        log("PropFlow is up.");
        log("");
        log($"  Open          {WebOrigin}");
        log($"  API           {ApiOrigin}  (health: {ApiOrigin}/health/ready)");
        if (seedDemo)
        {
            log("");
            log("  Sign in with organization slug + email + password:");
            log($"    tidewater-demo   demo-admin@tidewater.example.test        {secrets.DemoPassword}");
            log($"    tidewater-demo   demo-technician@tidewater.example.test   {secrets.DemoPassword}");
            log($"    isolation-demo   demo-admin@isolation.example.test        {secrets.DemoPassword}");
        }
        log("");
        log($"  Logs          {layout.LogDirectory}");
        log($"  Credentials   {layout.SecretsFile}");
        log("  Stop with     propflow-deploy down");
        return 0;
    }

    public async Task<int> DownAsync(bool removeData, CancellationToken cancellationToken)
    {
        StopTrackedProcesses();

        var secrets = File.Exists(layout.SecretsFile)
            ? DeploymentSecrets.LoadOrCreate(layout, _ => { })
            : null;

        if (secrets is not null && File.Exists(layout.ComposeFile))
        {
            // compose.yaml declares POSTGRES_PASSWORD as required, so even `down` needs it
            // interpolated or the command fails.
            string[] arguments = removeData ? ["down", "-v"] : ["down"];
            await ComposeAsync(secrets, arguments, cancellationToken).ConfigureAwait(false);
        }

        if (removeData)
        {
            log("Removed the database volume. The next `up` starts from an empty database.");
            log($"Left {layout.StateDirectory} in place — delete it by hand for a full reset.");
        }
        log("PropFlow is down.");
        return 0;
    }

    public async Task<int> StatusAsync(CancellationToken cancellationToken)
    {
        var problems = await Preflight.RunAsync(layout, requirePortsFree: false, cancellationToken).ConfigureAwait(false);
        foreach (var problem in problems) log($"  [!] {problem.What}");

        log($"  PostgreSQL  port {Preflight.DatabasePort}  {(Preflight.IsPortInUse(Preflight.DatabasePort) ? "listening" : "not listening")}");
        log($"  API         port {Preflight.ApiPort}  {(Preflight.IsPortInUse(Preflight.ApiPort) ? "listening" : "not listening")}");
        log($"  Web         port {Preflight.WebPort}  {(Preflight.IsPortInUse(Preflight.WebPort) ? "listening" : "not listening")}");

        if (!File.Exists(layout.SecretsFile))
        {
            log("  This deployment has never been brought up (no state/deployment.json).");
            return 0;
        }

        var secrets = DeploymentSecrets.LoadOrCreate(layout, _ => { });
        using var client = CreateHttpClient(secrets);
        foreach (var probe in new[] { "/health/live", "/health/ready", "/health/metrics" })
        {
            try
            {
                var response = await client.GetAsync(ApiOrigin + probe, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                log($"  GET {probe,-16} {(int)response.StatusCode}  {Truncate(body)}");
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                log($"  GET {probe,-16} unreachable ({error.GetType().Name})");
            }
        }
        return 0;
    }

    // ---- steps -------------------------------------------------------------------------------

    private async Task<bool> ComposeAsync(DeploymentSecrets secrets, string[] arguments, CancellationToken cancellationToken)
    {
        var docker = ProcessRunner.Locate("docker")!;
        var environment = new Dictionary<string, string> { ["POSTGRES_PASSWORD"] = secrets.PostgresPassword };
        string[] full = ["compose", "--file", layout.ComposeFile, .. arguments];
        var result = await ProcessRunner.RunAsync(docker, full, layout.Root, environment, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) log($"     docker compose {string.Join(' ', arguments)} failed: {result.Message}");
        return result.Succeeded;
    }

    private async Task<bool> AdminAsync(string verb, DeploymentSecrets secrets, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            layout.AdminExecutable, [verb], layout.AdminDirectory, AdminEnvironment(secrets), cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) log($"     {result.Message}");
        else log($"     {verb} failed: {result.Message}");
        return result.Succeeded;
    }

    private async Task<bool> BuildWebAsync(CancellationToken cancellationToken)
    {
        var npm = ProcessRunner.Locate("npm")!;
        if (!Directory.Exists(layout.WebNodeModules))
        {
            log("     npm ci");
            var install = await ProcessRunner.RunAsync(npm, ["ci"], layout.WebDirectory,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!install.Succeeded) { log($"     npm ci failed: {install.Message}"); return false; }
        }
        if (!Directory.Exists(layout.WebBuildOutput))
        {
            log("     next build");
            var build = await ProcessRunner.RunAsync(npm, ["run", "build"], layout.WebDirectory,
                WebEnvironment(), cancellationToken).ConfigureAwait(false);
            if (!build.Succeeded) { log($"     next build failed: {build.Message}"); return false; }
        }
        else
        {
            log("     Reusing the existing .next build.");
        }
        return true;
    }

    private Process StartApi(DeploymentSecrets secrets) => ProcessRunner.StartBackground(
        layout.ApiExecutable, [], layout.ApiDirectory, layout.ApiLog, ApiEnvironment(secrets));

    private Process StartWeb()
    {
        // Invoke Next's own entry point through node rather than `npx`, so nothing depends on
        // npx's resolution rules or reaches the network.
        var node = ProcessRunner.Locate("node")!;
        return ProcessRunner.StartBackground(
            node,
            [layout.NextBin, "start", "--hostname", "127.0.0.1", "--port", Preflight.WebPort.ToString()],
            layout.WebDirectory, layout.WebLog, WebEnvironment());
    }

    // ---- configuration -----------------------------------------------------------------------

    private string AdminConnectionString(DeploymentSecrets secrets) =>
        $"Host=127.0.0.1;Port={Preflight.DatabasePort};Database=propflow;Username=propflow;Password={secrets.PostgresPassword}";

    private string RuntimeConnectionString(DeploymentSecrets secrets) =>
        $"Host=127.0.0.1;Port={Preflight.DatabasePort};Database=propflow;Username=propflow_app;Password={secrets.RuntimePassword}";

    private Dictionary<string, string> AdminEnvironment(DeploymentSecrets secrets) => new()
    {
        ["ConnectionStrings__Admin"] = AdminConnectionString(secrets),
        ["Runtime__Password"] = secrets.RuntimePassword,
        ["Demo__Password"] = secrets.DemoPassword,
    };

    private Dictionary<string, string> ApiEnvironment(DeploymentSecrets secrets) => new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["ASPNETCORE_URLS"] = ApiOrigin,

        // Kestrel terminates TLS with the certificate this deployer issued. The SAN is
        // `localhost`, which is why the origin above is not 127.0.0.1.
        ["Kestrel__Certificates__Default__Path"] = layout.CertificatePfx,
        ["Kestrel__Certificates__Default__Password"] = secrets.CertificatePassword,

        // The API connects as the restricted role and refuses an owner/superuser connection.
        ["ConnectionStrings__Database"] = RuntimeConnectionString(secrets),

        // The three Production hard requirements.
        ["DataProtection__KeyPath"] = layout.DataProtectionKeyPath,
        ["ForwardedHeaders__Enabled"] = "true",
        ["ForwardedHeaders__KnownProxies"] = "127.0.0.1",
        ["Attachments__RootPath"] = layout.AttachmentsRootPath,

        // Signed provider delivery callbacks (PF-7.05). The providers themselves stay mocked.
        ["Communications__ProviderCallbackSecret"] = secrets.ProviderCallbackSecret,
    };

    private Dictionary<string, string> WebEnvironment() => new()
    {
        ["NODE_ENV"] = "production",
        ["PROPFLOW_API_ORIGIN"] = ApiOrigin,
        // undici ignores NODE_TLS_REJECT_UNAUTHORIZED, so pointing Node's CA store at our PEM is
        // the only thing that lets the /api proxy reach Kestrel.
        ["NODE_EXTRA_CA_CERTS"] = layout.CertificatePem,
    };

    // ---- readiness ---------------------------------------------------------------------------

    private async Task<bool> WaitForApiAsync(DeploymentSecrets secrets, CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient(secrets);
        // /health/ready stays 503 until the schema and the RLS ENABLE+FORCE checks pass, so this
        // gates on a usable API rather than on an open socket.
        return await PollAsync(
            async () =>
            {
                var response = await client.GetAsync(ApiOrigin + "/health/ready", cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            },
            "API readiness", 120, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WaitForWebAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return await PollAsync(
            async () =>
            {
                var response = await client.GetAsync(WebOrigin, cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            },
            "web app", 120, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PollAsync(Func<Task<bool>> probe, string what, int seconds, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= seconds; attempt++)
        {
            try
            {
                if (await probe().ConfigureAwait(false))
                {
                    log($"     {what} ready after {attempt}s.");
                    return true;
                }
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                // Not up yet.
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        log($"     {what} never became ready after {seconds}s.");
        return false;
    }

    private HttpClient CreateHttpClient(DeploymentSecrets secrets)
    {
        // Trust exactly the certificate this deployment issued, by thumbprint — not "accept
        // anything", which would hide a genuine misconfiguration.
        string? expected = null;
        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(layout.CertificatePfx, secrets.CertificatePassword);
            expected = certificate.Thumbprint;
        }
        catch (Exception error) when (error is CryptographicException or IOException)
        {
            // Fall through: with no expected thumbprint the callback rejects, which is correct.
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                expected is not null && presented is not null &&
                string.Equals(presented.Thumbprint, expected, StringComparison.OrdinalIgnoreCase),
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    // ---- process bookkeeping -----------------------------------------------------------------

    private void SaveProcessIds(Process api, Process web)
    {
        var ids = new Dictionary<string, int> { ["api"] = api.Id, ["web"] = web.Id };
        File.WriteAllText(layout.PidFile, JsonSerializer.Serialize(ids));
    }

    private void StopTrackedProcesses()
    {
        if (!File.Exists(layout.PidFile)) return;
        Dictionary<string, int>? ids;
        try
        {
            ids = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(layout.PidFile));
        }
        catch (JsonException)
        {
            File.Delete(layout.PidFile);
            return;
        }

        foreach (var (name, id) in ids ?? [])
        {
            try
            {
                using var process = Process.GetProcessById(id);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
                log($"Stopped the {name} process ({id}).");
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                // Already gone, or the id was recycled by an unrelated process — leave it alone.
            }
        }
        File.Delete(layout.PidFile);
    }

    // ---- reporting ---------------------------------------------------------------------------

    private int ReportPreflight(IReadOnlyList<PreflightProblem> problems)
    {
        log("Cannot deploy yet:");
        log("");
        foreach (var problem in problems)
        {
            log($"  - {problem.What}");
            log($"    {problem.Fix}");
        }
        return 1;
    }

    /// <summary>
    /// An absent log is not a clean log. Says which of those two happened rather than printing
    /// nothing and letting it read as success.
    /// </summary>
    private void DumpLog(string path, string what)
    {
        log("");
        if (!File.Exists(path))
        {
            log($"The {what} log does not exist at {path}. The process never started — this is NOT a clean run.");
            return;
        }
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            log($"The {what} log at {path} is empty. The process produced no output; that proves nothing about why it failed.");
            return;
        }
        log($"Last lines of the {what} log ({path}):");
        foreach (var line in lines.TakeLast(30)) log($"  {line}");
    }

    private static string Truncate(string value)
    {
        var flat = value.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 120 ? flat : flat[..120] + "…";
    }
}
