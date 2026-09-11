using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PropFlow.Deploy;

/// <summary>
/// Issues the TLS certificate Kestrel serves <c>https://localhost:5001</c> with.
///
/// Why self-signed and not <c>dotnet dev-certs</c>: a deployment bundle must not require the
/// .NET SDK on the target machine, and <c>dev-certs --trust</c> needs a keychain prompt. Nothing
/// here touches the OS trust store. The only process that must trust this certificate is Node —
/// <c>next dev</c>/<c>next start</c> proxies <c>/api</c> through undici, which ignores
/// <c>NODE_TLS_REJECT_UNAUTHORIZED</c> — so the PEM is handed to it via
/// <c>NODE_EXTRA_CA_CERTS</c> instead. The browser never reaches the API directly: it talks to
/// the web app over <c>http://127.0.0.1:3000</c>, a potentially-trustworthy origin, so the
/// <c>Secure</c> / <c>__Host-</c> cookies the API sets through the proxy are still accepted.
///
/// The subject alternative name is <c>localhost</c> only, which is why the API must be addressed
/// as <c>localhost</c> and never <c>127.0.0.1</c>.
/// </summary>
internal static class CertificateFactory
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    public static void EnsureCertificate(DeploymentLayout layout, DeploymentSecrets secrets, Action<string> log)
    {
        if (File.Exists(layout.CertificatePfx) && File.Exists(layout.CertificatePem) && NotExpiringSoon(layout, secrets))
        {
            log($"Reusing the TLS certificate in {layout.CertificateDirectory}.");
            return;
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid(ServerAuthenticationOid)], critical: true));

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());

        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));

        Directory.CreateDirectory(layout.CertificateDirectory);
        File.WriteAllBytes(layout.CertificatePfx, certificate.Export(X509ContentType.Pfx, secrets.CertificatePassword));
        File.WriteAllText(layout.CertificatePem, new string(PemEncoding.Write("CERTIFICATE", certificate.RawData)));
        log($"Issued a self-signed TLS certificate for localhost, valid one year, in {layout.CertificateDirectory}.");
    }

    /// <summary>
    /// A certificate inside its last 30 days is reissued rather than carried into an expiry that
    /// would surface as an opaque TLS failure from Node halfway through a demo.
    /// </summary>
    private static bool NotExpiringSoon(DeploymentLayout layout, DeploymentSecrets secrets)
    {
        try
        {
            using var existing = X509CertificateLoader.LoadPkcs12FromFile(
                layout.CertificatePfx, secrets.CertificatePassword);
            return existing.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(30);
        }
        catch (CryptographicException)
        {
            // Unreadable or issued against different credentials. Reissue.
            return false;
        }
    }
}
