namespace PropFlow.Deploy;

/// <summary>
/// Every path the deployer reads or writes, resolved from the bundle root.
///
/// The bundle root is the directory holding the deployer itself. A published bundle looks like:
///
///   propflow-deploy(.exe)   this program
///   api/PropFlow.Api        self-contained API build
///   admin/PropFlow.Admin    self-contained migration/seed tool
///   web/                    Next.js source; npm ci + next build run on first `up`
///   compose.yaml            PostgreSQL only
///   state/                  created on first run, never in source control
///
/// `state/` is the whole mutable surface: the Data Protection key ring, attachment blobs, the
/// TLS certificate and the generated credentials. Deleting it is a full factory reset.
/// </summary>
internal sealed class DeploymentLayout
{
    private DeploymentLayout(string root) => Root = root;

    public string Root { get; }

    public string ComposeFile => Path.Combine(Root, "compose.yaml");
    public string ApiDirectory => Path.Combine(Root, "api");
    public string AdminDirectory => Path.Combine(Root, "admin");
    public string WebDirectory => Path.Combine(Root, "web");

    public string StateDirectory => Path.Combine(Root, "state");
    public string SecretsFile => Path.Combine(StateDirectory, "deployment.json");
    public string DataProtectionKeyPath => Path.Combine(StateDirectory, "dataprotection-keys");
    public string AttachmentsRootPath => Path.Combine(StateDirectory, "attachments");
    public string CertificateDirectory => Path.Combine(StateDirectory, "certs");
    public string CertificatePfx => Path.Combine(CertificateDirectory, "propflow.pfx");
    public string CertificatePem => Path.Combine(CertificateDirectory, "propflow.pem");
    public string LogDirectory => Path.Combine(StateDirectory, "logs");
    public string ApiLog => Path.Combine(LogDirectory, "api.log");
    public string WebLog => Path.Combine(LogDirectory, "web.log");
    public string PidFile => Path.Combine(StateDirectory, "processes.json");

    /// <summary>The published API executable, named for the current platform.</summary>
    public string ApiExecutable => Path.Combine(ApiDirectory, Executable("PropFlow.Api"));

    /// <summary>The published admin executable, named for the current platform.</summary>
    public string AdminExecutable => Path.Combine(AdminDirectory, Executable("PropFlow.Admin"));

    /// <summary>Next's own entry point. Invoked through `node` so no npx resolution is involved.</summary>
    public string NextBin => Path.Combine(WebDirectory, "node_modules", "next", "dist", "bin", "next");

    public string WebNodeModules => Path.Combine(WebDirectory, "node_modules");
    public string WebBuildOutput => Path.Combine(WebDirectory, ".next");

    private static string Executable(string name) =>
        OperatingSystem.IsWindows() ? name + ".exe" : name;

    public static DeploymentLayout FromExecutableLocation() =>
        new(Path.GetDirectoryName(Environment.ProcessPath) ?? Directory.GetCurrentDirectory());

    public static DeploymentLayout At(string root) => new(Path.GetFullPath(root));

    public void EnsureStateDirectories()
    {
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(DataProtectionKeyPath);
        Directory.CreateDirectory(AttachmentsRootPath);
        Directory.CreateDirectory(CertificateDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    /// <summary>
    /// Names what a bundle is missing, so a broken download fails with a list rather than a
    /// confusing "file not found" three steps later.
    /// </summary>
    public IReadOnlyList<string> MissingComponents()
    {
        var missing = new List<string>();
        if (!File.Exists(ComposeFile)) missing.Add($"compose.yaml (expected at {ComposeFile})");
        if (!File.Exists(ApiExecutable)) missing.Add($"the API build (expected at {ApiExecutable})");
        if (!File.Exists(AdminExecutable)) missing.Add($"the admin tool (expected at {AdminExecutable})");
        if (!File.Exists(Path.Combine(WebDirectory, "package.json")))
            missing.Add($"the web app (expected at {Path.Combine(WebDirectory, "package.json")})");
        return missing;
    }
}
