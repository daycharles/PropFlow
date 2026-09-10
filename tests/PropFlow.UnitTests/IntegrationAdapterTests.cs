using PropFlow.Application.Integrations;
using PropFlow.Infrastructure.Integrations;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class IntegrationAdapterTests
{
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

        var first = await adapter.PullAsync(default);
        var second = await adapter.PullAsync(default);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Mock_snapshot_is_internally_consistent()
    {
        var snapshot = await new MockIntegrationAdapter().PullAsync(default);
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
}
