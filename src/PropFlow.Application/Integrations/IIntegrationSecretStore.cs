namespace PropFlow.Application.Integrations;

// Where an adapter gets the credential for one connection.
//
// Why this port exists, stated plainly because the repo's own docs are wrong about it:
// `docs/milestones.md:61,63` lists "managed secrets" under PF-7.02 and reports PF-7.02 delivered.
// Against the code it was not. What PF-7.02 actually shipped is
//   - refuse-to-boot configuration gating (`src/PropFlow.Api/DeploymentConfiguration.cs`), which
//     validates what is configured but never resolves a secret,
//   - global, per-deployment provider credentials as plain configuration properties
//     (`CommunicationsOptions.TwilioAuthToken`, `.SendGridApiKey`), which are one-per-process and
//     cannot be keyed per connection, and
//   - a deployment-time credential generator that disclaims itself in as many words —
//     `tools/PropFlow.Deploy/DeploymentSecrets.cs:11-13`: "Nothing here is a production
//     secret-management story — a real deployment takes these from a secret manager (PF-7.02)."
// There is no `ISecretStore` anywhere in `src/` (verified against the tree, 2026-09-12). So the
// per-connection credential lookup an HTTP adapter needs has no home, and this is it.
//
// The boundary, so nobody builds the text box: **there will be no credential column on
// `integrations."Connections"`.** `IntegrationConnection` already documents that it stores no
// credentials. A UI where an admin types a credential that PropFlow then holds needs envelope
// encryption, a key-rotation story and a deletion path — a later story, not this one. A secret is
// supplied to the deployment out of band and resolved here by connection id.
//
// PF-S19.07 ships the configuration-backed implementation. This file is the contract only.
public interface IIntegrationSecretStore
{
    // Resolve the credential configured for `connectionId`. Never throws for a missing secret —
    // "not configured" is an ordinary answer the caller turns into a disabled connection or a
    // sync failure with a useful message, following the outcome-enum convention the rest of the
    // Application layer uses rather than exceptions for expected states.
    Task<IntegrationSecretLookup> ResolveAsync(Guid connectionId, CancellationToken cancellationToken);
}

public enum IntegrationSecretOutcome
{
    // A non-empty credential is configured for this connection and is in `Secret`.
    Found = 0,

    // No credential is configured for this connection. Distinct from an empty string: an adapter
    // that needs one must fail the sync with "no credential configured", not send a blank header.
    NotConfigured = 1
}

public sealed record IntegrationSecretLookup(IntegrationSecretOutcome Outcome, string? Secret)
{
    public static IntegrationSecretLookup NotConfigured { get; } = new(IntegrationSecretOutcome.NotConfigured, null);

    public static IntegrationSecretLookup Found(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("A resolved integration secret must not be empty.", nameof(secret));
        return new IntegrationSecretLookup(IntegrationSecretOutcome.Found, secret);
    }
}
