using PropFlow.Application.Integrations;
using PropFlow.Domain.Integrations;
using PropFlow.Infrastructure.Integrations;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class IntegrationAdapterTests
{
    // The mock adapter returns the same snapshot for every connection, so any id will do.
    private static readonly IntegrationPullContext AnyConnection = new(Guid.NewGuid());

    [Fact]
    public void Canonical_hash_is_stable_for_equal_records_and_changes_with_content()
    {
        var a = new CanonicalProperty("P1", "Cedar Court", "1 A St", "Norfolk", "VA", "23510", "America/New_York");
        var same = a with { };
        var different = a with { Name = "Cedar Terrace" };

        Assert.Equal(CanonicalHash.Of(a), CanonicalHash.Of(same));
        Assert.NotEqual(CanonicalHash.Of(a), CanonicalHash.Of(different));
    }

    [Fact]
    public async Task Mock_adapter_identifies_as_mock_and_is_deterministic()
    {
        var adapter = new MockIntegrationAdapter();
        Assert.Equal("mock", adapter.SourceSystem);

        var first = await adapter.PullAsync(AnyConnection, default);
        var second = await adapter.PullAsync(AnyConnection, default);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Mock_snapshot_is_internally_consistent()
    {
        var snapshot = await new MockIntegrationAdapter().PullAsync(AnyConnection, default);
        var propertyIds = snapshot.Properties.Select(p => p.ExternalId).ToHashSet();
        var spaceIds = snapshot.Spaces.Select(s => s.ExternalId).ToHashSet();

        Assert.NotEmpty(snapshot.Properties);
        Assert.All(snapshot.Spaces, s => Assert.Contains(s.PropertyExternalId, propertyIds));
        Assert.All(snapshot.Occupancies, o => Assert.Contains(o.SpaceExternalId, spaceIds));
        Assert.All(snapshot.WorkOrders, w => Assert.Contains(w.PropertyExternalId, propertyIds));
        Assert.All(snapshot.WorkOrders, w => Assert.True(w.SpaceExternalId is null || spaceIds.Contains(w.SpaceExternalId)));
        Assert.All(snapshot.Assets, a => Assert.Contains(a.PropertyExternalId, propertyIds));
        Assert.All(snapshot.Assets, a => Assert.True(a.SpaceExternalId is null || spaceIds.Contains(a.SpaceExternalId)));
    }

    [Fact]
    public void Catalog_resolves_registered_adapters_by_source_and_returns_null_otherwise()
    {
        var catalog = new IntegrationCatalog([new MockIntegrationAdapter()]);
        Assert.Single(catalog.Available);
        Assert.NotNull(catalog.Resolve("mock"));
        Assert.Null(catalog.Resolve("yardi"));
    }

    [Fact]
    public void Mock_adapter_declares_a_full_property_management_snapshot()
    {
        var descriptor = new MockIntegrationAdapter().Descriptor;

        Assert.Equal(IntegrationCategory.PropertyManagement, descriptor.Category);
        Assert.False(descriptor.SupportsIncrementalPull);
        Assert.Equal(IntegrationAdapterDescriptor.AllKinds, descriptor.SuppliedKinds);
        Assert.All(IntegrationAdapterDescriptor.AllKinds, kind => Assert.True(descriptor.Supplies(kind)));
    }

    [Fact]
    public void Descriptor_reports_a_kind_it_does_not_supply()
    {
        var descriptor = new IntegrationAdapterDescriptor(
            IntegrationCategory.PropertyManagement,
            [IntegrationEntityKind.Property, IntegrationEntityKind.Space],
            SupportsIncrementalPull: true);

        Assert.True(descriptor.Supplies(IntegrationEntityKind.Property));
        Assert.False(descriptor.Supplies(IntegrationEntityKind.WorkOrder));
        Assert.True(descriptor.SupportsIncrementalPull);
    }

    [Fact]
    public void Category_taxonomy_is_the_nine_categories_and_reserves_screening()
    {
        var categories = Enum.GetValues<IntegrationCategory>();

        Assert.Equal(9, categories.Length);
        Assert.Contains(IntegrationCategory.Screening, categories);
        // Screening stays a reserved slot: it is FS-S05's IScreeningProvider, not an adapter.
        Assert.NotEqual(IntegrationCategory.Screening, new MockIntegrationAdapter().Descriptor.Category);
    }

    [Fact]
    public void Catalog_rejects_two_adapters_claiming_one_source_system_and_names_both()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new IntegrationCatalog([new MockIntegrationAdapter(), new DuplicateSourceAdapter()]));

        Assert.Contains("'mock'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(MockIntegrationAdapter).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(DuplicateSourceAdapter).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_keeps_distinct_sources_in_registration_order()
    {
        var second = new SandboxStubAdapter();
        var catalog = new IntegrationCatalog([new MockIntegrationAdapter(), second]);

        Assert.Equal(["mock", "stub"], catalog.Available.Select(a => a.SourceSystem));
        Assert.Same(second, catalog.Resolve("stub"));
    }

    [Fact]
    public void Secret_lookup_distinguishes_a_configured_secret_from_none()
    {
        Assert.Equal(IntegrationSecretOutcome.NotConfigured, IntegrationSecretLookup.NotConfigured.Outcome);
        Assert.Null(IntegrationSecretLookup.NotConfigured.Secret);

        var found = IntegrationSecretLookup.Found("s3cret-token");
        Assert.Equal(IntegrationSecretOutcome.Found, found.Outcome);
        Assert.Equal("s3cret-token", found.Secret);

        Assert.Throws<ArgumentException>(() => IntegrationSecretLookup.Found("  "));
    }

    [Fact]
    public void Pull_context_requires_the_connection_it_runs_for()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, new IntegrationPullContext(id).ConnectionId);
        Assert.Throws<ArgumentException>(() => new IntegrationPullContext(Guid.Empty));
    }

    // Claims "mock" on purpose — the misconfiguration IntegrationCatalog must refuse loudly.
    private sealed class DuplicateSourceAdapter : IIntegrationAdapter
    {
        public string SourceSystem => MockIntegrationAdapter.Source;
        public string DisplayName => "Second mock";
        public IntegrationAdapterDescriptor Descriptor => IntegrationAdapterDescriptor.FullPropertyManagementSnapshot;

        public Task<IntegrationSnapshot> PullAsync(IntegrationPullContext context, CancellationToken cancellationToken) =>
            Task.FromResult(IntegrationSnapshot.Empty(SourceSystem));
    }

    private sealed class SandboxStubAdapter : IIntegrationAdapter
    {
        public string SourceSystem => "stub";
        public string DisplayName => "Stub property system";
        public IntegrationAdapterDescriptor Descriptor => IntegrationAdapterDescriptor.FullPropertyManagementSnapshot;

        public Task<IntegrationSnapshot> PullAsync(IntegrationPullContext context, CancellationToken cancellationToken) =>
            Task.FromResult(IntegrationSnapshot.Empty(SourceSystem));
    }
}
