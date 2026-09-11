using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PropFlow.Deploy;

/// <summary>
/// The credentials this deployment runs on. Generated once on first <c>up</c> and persisted under
/// <c>state/</c> so a later <c>up</c> reconnects to the same database instead of locking itself out.
///
/// Nothing here is a production secret-management story — a real deployment takes these from a
/// secret manager (PF-7.02). This exists so a single-host install has distinct, non-default,
/// length-valid credentials rather than the ones printed in a public document.
/// </summary>
internal sealed record DeploymentSecrets
{
    /// <summary>Owner role password. Used only by the admin tool, never by the API.</summary>
    [JsonPropertyName("postgresPassword")]
    public required string PostgresPassword { get; init; }

    /// <summary>
    /// Restricted runtime role password. <see cref="Validate"/> enforces the 20-character floor
    /// that <c>DatabaseProvisioner.ConfigureRuntimeAsync</c> rejects below.
    /// </summary>
    [JsonPropertyName("runtimePassword")]
    public required string RuntimePassword { get; init; }

    /// <summary>Password for all three seeded demo accounts. 12-character floor.</summary>
    [JsonPropertyName("demoPassword")]
    public required string DemoPassword { get; init; }

    /// <summary>Protects the PFX on disk. Not a transport secret.</summary>
    [JsonPropertyName("certificatePassword")]
    public required string CertificatePassword { get; init; }

    /// <summary>Signs provider delivery callbacks (PF-7.05).</summary>
    [JsonPropertyName("providerCallbackSecret")]
    public required string ProviderCallbackSecret { get; init; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static DeploymentSecrets LoadOrCreate(DeploymentLayout layout, Action<string> log)
    {
        if (File.Exists(layout.SecretsFile))
        {
            var existing = JsonSerializer.Deserialize<DeploymentSecrets>(File.ReadAllText(layout.SecretsFile))
                ?? throw new InvalidOperationException(
                    $"{layout.SecretsFile} exists but could not be read. Delete the state directory to start over.");
            existing.Validate();
            log($"Reusing the credentials in {layout.SecretsFile}.");
            return existing;
        }

        var created = new DeploymentSecrets
        {
            // 32 characters clears the 20-character runtime floor with room to spare, and the
            // demo password gets a fixed punctuation/digit/case sample so it satisfies the
            // ASP.NET Identity complexity rules the seeder applies.
            PostgresPassword = Generate(32),
            RuntimePassword = Generate(32),
            DemoPassword = "Demo!" + Generate(16) + "9aZ",
            CertificatePassword = Generate(32),
            ProviderCallbackSecret = Generate(48),
        };
        created.Validate();

        Directory.CreateDirectory(layout.StateDirectory);
        File.WriteAllText(layout.SecretsFile, JsonSerializer.Serialize(created, Json));
        RestrictToOwner(layout.SecretsFile);
        log($"Generated new credentials and wrote them to {layout.SecretsFile}.");
        return created;
    }

    public void Validate()
    {
        if (RuntimePassword.Length < 20)
            throw new InvalidOperationException(
                "runtimePassword must be at least 20 characters; configure-runtime refuses anything shorter.");
        if (DemoPassword.Length < 12)
            throw new InvalidOperationException("demoPassword must be at least 12 characters.");
        if (string.IsNullOrWhiteSpace(PostgresPassword))
            throw new InvalidOperationException("postgresPassword must not be empty.");
    }

    /// <summary>
    /// Best effort 0600 on the credentials file. Unix only — on Windows the file inherits the
    /// user profile's ACL, and silently doing nothing is the correct behaviour there.
    /// </summary>
    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A restrictive umask or an exotic filesystem is not a reason to abort a deployment.
        }
    }

    /// <summary>URL-safe, no ambiguous characters, and safe to paste into a connection string.</summary>
    private static string Generate(int length)
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return RandomNumberGenerator.GetString(alphabet, length);
    }
}
