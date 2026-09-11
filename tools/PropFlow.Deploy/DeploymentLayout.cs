namespace PropFlow.Deploy;

/// <summary>
/// Every path the deployer reads or writes.
///
/// A bundle looks like this, and the directory holding the deployer is its <see cref="Root"/>:
///
///   propflow-deploy(.exe)   this program
///   api/PropFlow.Api        self-contained API build
///   admin/PropFlow.Admin    self-contained migration/seed tool
///   web/                    Next.js source; npm ci + next build run on first `up`
///   compose.yaml            PostgreSQL only
///
/// <b>Root is not always writable.</b> Unpack a release archive and it is; install the MSI and
/// the application lands under <c>C:\Program Files</c>, which is read-only for a non-elevated
/// user. Everything mutable therefore resolves through <see cref="StateRoot"/>, which is chosen
/// once by <see cref="Resolve"/>:
///
///   1. an explicit <c>--state &lt;path&gt;</c> or <c>PROPFLOW_STATE</c>, if given;
///   2. <c>&lt;root&gt;/state</c> when the bundle directory is writable — the unpacked-archive
///      case, which keeps a self-contained folder self-contained;
///   3. otherwise the per-user/per-machine application data directory — <c>%ProgramData%\PropFlow</c>
///      on Windows, <c>~/Library/Application Support/PropFlow</c> on macOS, <c>~/.local/share/propflow</c>
///      elsewhere.
///
/// The same split applies to the web app: <c>npm ci</c> and <c>next build</c> write
/// <c>node_modules</c> and <c>.next</c> into the web directory, so when the bundle is read-only
/// the pristine source is copied to <see cref="WebDirectory"/> under the state root and built
/// there. <see cref="SeedWebSourceIfNeeded"/> does that copy.
/// </summary>
internal sealed class DeploymentLayout
{
    private DeploymentLayout(string root, string stateRoot, bool bundleIsWritable)
    {
        Root = root;
        StateRoot = stateRoot;
        BundleIsWritable = bundleIsWritable;
    }

    /// <summary>Where the shipped, possibly read-only, files live.</summary>
    public string Root { get; }

    /// <summary>Where everything this deployment creates lives.</summary>
    public string StateRoot { get; }

    /// <summary>False when the application is installed somewhere the user cannot write.</summary>
    public bool BundleIsWritable { get; }

    // ---- shipped, read-only -------------------------------------------------------------------

    public string ComposeFile => Path.Combine(Root, "compose.yaml");
    public string ApiDirectory => Path.Combine(Root, "api");
    public string AdminDirectory => Path.Combine(Root, "admin");

    /// <summary>The pristine web source as shipped. Never built in place when read-only.</summary>
    public string WebSourceDirectory => Path.Combine(Root, "web");

    public string ApiExecutable => Path.Combine(ApiDirectory, Executable("PropFlow.Api"));
    public string AdminExecutable => Path.Combine(AdminDirectory, Executable("PropFlow.Admin"));

    // ---- mutable ------------------------------------------------------------------------------

    /// <summary>The web copy that actually gets <c>npm ci</c>'d and built.</summary>
    public string WebDirectory => BundleIsWritable ? WebSourceDirectory : Path.Combine(StateRoot, "web");

    public string SecretsFile => Path.Combine(StateRoot, "deployment.json");
    public string DataProtectionKeyPath => Path.Combine(StateRoot, "dataprotection-keys");
    public string AttachmentsRootPath => Path.Combine(StateRoot, "attachments");
    public string CertificateDirectory => Path.Combine(StateRoot, "certs");
    public string CertificatePfx => Path.Combine(CertificateDirectory, "propflow.pfx");
    public string CertificatePem => Path.Combine(CertificateDirectory, "propflow.pem");
    public string LogDirectory => Path.Combine(StateRoot, "logs");
    public string ApiLog => Path.Combine(LogDirectory, "api.log");
    public string WebLog => Path.Combine(LogDirectory, "web.log");
    public string PidFile => Path.Combine(StateRoot, "processes.json");

    public string WebNodeModules => Path.Combine(WebDirectory, "node_modules");
    public string WebBuildOutput => Path.Combine(WebDirectory, ".next");

    /// <summary>Next's own entry point. Invoked through <c>node</c> so no npx resolution applies.</summary>
    public string NextBin => Path.Combine(WebDirectory, "node_modules", "next", "dist", "bin", "next");

    private static string Executable(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    // ---- resolution ---------------------------------------------------------------------------

    /// <summary>
    /// Picks the bundle root and the state root. <paramref name="explicitRoot"/> and
    /// <paramref name="explicitState"/> come from <c>--root</c> / <c>--state</c>; the
    /// <c>PROPFLOW_STATE</c> environment variable is the fallback for the latter.
    /// </summary>
    public static DeploymentLayout Resolve(string? explicitRoot = null, string? explicitState = null)
    {
        var root = explicitRoot is null
            ? Path.GetDirectoryName(Environment.ProcessPath) ?? Directory.GetCurrentDirectory()
            : Path.GetFullPath(explicitRoot);

        var writable = IsWritable(root);

        var state = explicitState ?? Environment.GetEnvironmentVariable("PROPFLOW_STATE");
        if (!string.IsNullOrWhiteSpace(state)) return new DeploymentLayout(root, Path.GetFullPath(state), writable);

        return new DeploymentLayout(root, writable ? Path.Combine(root, "state") : ApplicationDataRoot(), writable);
    }

    private static string ApplicationDataRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PropFlow");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", "PropFlow")
            : Path.Combine(home, ".local", "share", "propflow");
    }

    /// <summary>
    /// Writability is established by actually writing, not by inspecting an ACL. On Windows a
    /// permission check that reasons about groups gets virtualization and inherited denies wrong;
    /// creating and deleting a file does not.
    /// </summary>
    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".propflow-write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    // ---- preparation --------------------------------------------------------------------------

    public void EnsureStateDirectories()
    {
        Directory.CreateDirectory(StateRoot);
        Directory.CreateDirectory(DataProtectionKeyPath);
        Directory.CreateDirectory(AttachmentsRootPath);
        Directory.CreateDirectory(CertificateDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    /// <summary>
    /// When the bundle is read-only, copies the shipped web source into the state root once so it
    /// can be installed and built. Does nothing in the writable case, where the shipped directory
    /// is already the working one. <c>node_modules</c> and <c>.next</c> are never copied — they
    /// are build output and are regenerated on the target.
    /// </summary>
    public void SeedWebSourceIfNeeded(Action<string> log)
    {
        if (BundleIsWritable) return;
        if (File.Exists(Path.Combine(WebDirectory, "package.json"))) return;

        log($"     Copying the web source to {WebDirectory} (the installed copy is read-only).");
        CopyDirectory(WebSourceDirectory, WebDirectory);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is "node_modules" or ".next") continue;
            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }

    /// <summary>
    /// Names what a bundle is missing, so a broken install fails with a list rather than a
    /// confusing "file not found" three steps later.
    /// </summary>
    public IReadOnlyList<string> MissingComponents()
    {
        var missing = new List<string>();
        // Reported through the same "missing X" sentence as the rest, so it has to read as a noun.
        if (!Directory.Exists(Root)) return [$"the bundle directory itself ({Root} does not exist)"];
        if (!File.Exists(ComposeFile)) missing.Add($"compose.yaml (expected at {ComposeFile})");
        if (!File.Exists(ApiExecutable)) missing.Add($"the API build (expected at {ApiExecutable})");
        if (!File.Exists(AdminExecutable)) missing.Add($"the admin tool (expected at {AdminExecutable})");
        if (!File.Exists(Path.Combine(WebSourceDirectory, "package.json")))
            missing.Add($"the web app (expected at {Path.Combine(WebSourceDirectory, "package.json")})");
        return missing;
    }
}
