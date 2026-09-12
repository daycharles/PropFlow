using Microsoft.Extensions.Configuration;
using PropFlow.Application.Integrations;

namespace PropFlow.Infrastructure.Integrations;

// The configuration-backed `IIntegrationSecretStore`, keyed by connection id:
//
//     Integrations:Secrets:<connection id> = <credential>
//
// and as an environment variable, which is how a deployment actually supplies it:
//
//     Integrations__Secrets__8f1d...  (the connection's Guid in "D" form, e.g. 8f1d…-…-…)
//
// Keying by connection id is the whole point of the port, and what makes this different from
// `CommunicationsOptions.TwilioAuthToken` / `.SendGridApiKey`: those are one set per process, so
// two organizations pulling from the same source system would necessarily share a credential.
// Closes the `docs/followups.md` item "Integration connections store no credentials".
//
// The credential is supplied to the deployment out of band and never round-trips through
// PropFlow: **there is no credential column on `integrations."Connections"`**, and this store
// has no write path on purpose. A UI where an admin types a credential needs envelope
// encryption, key rotation and a deletion path — a later story. Configuration is the same trust
// boundary the database password and the provider API keys already sit behind.
//
// .NET configuration keys are case-insensitive, so a key written with an upper-case Guid
// resolves the same as the lower-case "D" form `Guid.ToString()` produces.
public sealed class ConfiguredIntegrationSecretStore(IConfiguration configuration) : IIntegrationSecretStore
{
    public const string SectionName = "Integrations:Secrets";

    public static string KeyFor(Guid connectionId) => $"{SectionName}:{connectionId:D}";

    public Task<IntegrationSecretLookup> ResolveAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var value = configuration[KeyFor(connectionId)];
        // Whitespace-only counts as not configured: an operator who set the variable to "" meant
        // "none", and sending a blank credential produces a 401 the adapter cannot explain.
        return Task.FromResult(string.IsNullOrWhiteSpace(value)
            ? IntegrationSecretLookup.NotConfigured
            : IntegrationSecretLookup.Found(value));
    }
}
