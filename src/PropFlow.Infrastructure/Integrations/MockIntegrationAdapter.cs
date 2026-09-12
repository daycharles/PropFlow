using PropFlow.Application.Integrations;

namespace PropFlow.Infrastructure.Integrations;

// The one adapter milestone 6 ships. It returns a fixed, self-consistent snapshot (parents
// referenced by every child exist) so the sync path, the health screen and the tests have
// something real to run against without a live external system. Deterministic: the same call
// always yields the same records with the same content, so a re-sync reports zero changes.
public sealed class MockIntegrationAdapter : IIntegrationAdapter
{
    public const string Source = "mock";

    public string SourceSystem => Source;
    public string DisplayName => "Mock property system";

    // Property management, all five canonical kinds, whole-source snapshot only: PullAsync takes
    // no cursor and always returns the same fixed set.
    public IntegrationAdapterDescriptor Descriptor => IntegrationAdapterDescriptor.FullPropertyManagementSnapshot;

    public Task<IntegrationSnapshot> PullAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Snapshot);

    private static readonly IntegrationSnapshot Snapshot = new(
        Source,
        Properties:
        [
            new CanonicalProperty("MOCK-PROP-1", "Cedar Court", "100 Cedar St", "Norfolk", "VA", "23510", "America/New_York"),
            new CanonicalProperty("MOCK-PROP-2", "Birch Terrace", "220 Birch Ave", "Norfolk", "VA", "23517", "America/New_York")
        ],
        Spaces:
        [
            new CanonicalSpace("MOCK-SPACE-1", "MOCK-PROP-1", "101", null),
            new CanonicalSpace("MOCK-SPACE-2", "MOCK-PROP-1", "102", null),
            new CanonicalSpace("MOCK-SPACE-3", "MOCK-PROP-2", "1A", null)
        ],
        Occupancies:
        [
            new CanonicalOccupancy("MOCK-OCC-1", "MOCK-SPACE-1", "Alex Turner", "alex.turner@example.test", "+15550110001",
                new DateOnly(2025, 3, 1), null),
            new CanonicalOccupancy("MOCK-OCC-2", "MOCK-SPACE-3", "Priya Nair", "priya.nair@example.test", null,
                new DateOnly(2024, 11, 15), null)
        ],
        WorkOrders:
        [
            new CanonicalWorkOrder("MOCK-WO-1", "MOCK-PROP-1", "MOCK-SPACE-1", "Leaking kitchen faucet",
                "Reported by resident, steady drip", "Open", new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero), null),
            new CanonicalWorkOrder("MOCK-WO-2", "MOCK-PROP-2", null, "Annual fire-alarm inspection",
                null, "Closed", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 1, 11, 30, 0, TimeSpan.Zero))
        ],
        Assets:
        [
            new CanonicalAsset("MOCK-ASSET-1", "MOCK-PROP-1", "MOCK-SPACE-1", "Water heater", "WaterHeater",
                "Rheem", "XE40", "RH-4021-8873", new DateOnly(2021, 5, 10)),
            new CanonicalAsset("MOCK-ASSET-2", "MOCK-PROP-2", null, "Rooftop HVAC", "Hvac",
                "Carrier", "48TC", "CR-9932-1120", new DateOnly(2019, 9, 2))
        ]);
}
