using PropFlow.Application.Integrations;

namespace PropFlow.Infrastructure.Integrations;

// The adapters wired into this deployment. Every IIntegrationAdapter registered in DI is
// discoverable here and resolvable by its source-system id.
public sealed class IntegrationCatalog(IEnumerable<IIntegrationAdapter> adapters) : IIntegrationCatalog
{
    private readonly Dictionary<string, IIntegrationAdapter> bySource =
        adapters.ToDictionary(a => a.SourceSystem, StringComparer.Ordinal);

    public IReadOnlyList<IIntegrationAdapter> Available => bySource.Values.ToList();

    public IIntegrationAdapter? Resolve(string sourceSystem) =>
        bySource.GetValueOrDefault(sourceSystem);
}
