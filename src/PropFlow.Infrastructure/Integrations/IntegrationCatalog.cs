using PropFlow.Application.Integrations;

namespace PropFlow.Infrastructure.Integrations;

// The adapters wired into this deployment. Every IIntegrationAdapter registered in DI is
// discoverable here and resolvable by its source-system id.
//
// Two adapters claiming one source system is a deployment configuration bug and must be loud:
// `Resolve` could only ever return one of them, so a connection created against that source
// silently syncs from whichever registration happened to win. The previous `ToDictionary` threw
// an opaque `ArgumentException` ("An item with the same key has already been added") from a DI
// constructor, which surfaces as an unexplained startup crash naming neither the source system
// nor the two types. Latent while only the mock adapter registered; reachable the moment
// PF-S19.07 registers the sandbox HTTP adapter. Keeping the last one instead would be worse —
// the deployment would boot and be wrong.
public sealed class IntegrationCatalog : IIntegrationCatalog
{
    private readonly Dictionary<string, IIntegrationAdapter> bySource;

    public IntegrationCatalog(IEnumerable<IIntegrationAdapter> adapters)
    {
        bySource = new Dictionary<string, IIntegrationAdapter>(StringComparer.Ordinal);
        var ordered = new List<IIntegrationAdapter>();
        foreach (var adapter in adapters)
        {
            if (bySource.TryGetValue(adapter.SourceSystem, out var existing))
                throw new InvalidOperationException(
                    $"Two integration adapters are registered for source system '{adapter.SourceSystem}': " +
                    $"{existing.GetType().FullName} and {adapter.GetType().FullName}. " +
                    "Each adapter must declare a unique SourceSystem.");

            bySource.Add(adapter.SourceSystem, adapter);
            ordered.Add(adapter);
        }

        Available = ordered;
    }

    // Registration order, fixed at construction.
    public IReadOnlyList<IIntegrationAdapter> Available { get; }

    public IIntegrationAdapter? Resolve(string sourceSystem) =>
        bySource.GetValueOrDefault(sourceSystem);
}
