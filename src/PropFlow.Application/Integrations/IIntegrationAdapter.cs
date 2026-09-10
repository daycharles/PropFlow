namespace PropFlow.Application.Integrations;

// An adapter reads one external property-management system and returns its records as a
// canonical snapshot. Pull-only for now: PropFlow is the system of record and does not write
// back. Implementations live in Infrastructure; the mock adapter is the only one in milestone 6.
public interface IIntegrationAdapter
{
    // Machine identifier, lower-case, matching IntegrationConnection.SourceSystem ("mock").
    string SourceSystem { get; }

    // Human label for the Integration Health screen ("Mock property system").
    string DisplayName { get; }

    Task<IntegrationSnapshot> PullAsync(CancellationToken cancellationToken);
}

// The set of adapters wired into this deployment. Backed by DI registration.
public interface IIntegrationCatalog
{
    IReadOnlyList<IIntegrationAdapter> Available { get; }

    IIntegrationAdapter? Resolve(string sourceSystem);
}
