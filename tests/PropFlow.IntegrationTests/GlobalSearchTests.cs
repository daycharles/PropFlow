using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class GlobalSearchTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement[]> Search(Scenario s, string q, int? limit = null)
    {
        var url = $"/api/search?q={Uri.EscapeDataString(q)}" + (limit is { } l ? $"&limit={l}" : "");
        return (await s.Client.GetFromJsonAsync<JsonElement[]>(url))!;
    }

    private static bool Has(JsonElement[] hits, string type, string label) =>
        hits.Any(h => h.GetProperty("type").GetString() == type
            && h.GetProperty("label").GetString()!.Contains(label, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Finds_substring_matches_across_entity_types()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var hits = await Search(s, "pest");
        Assert.True(Has(hits, "Vendor", "Tidewater Pest Services"));
        Assert.True(Has(hits, "Work", "Pest control"));

        var byName = await Search(s, "Dana Rey");
        Assert.True(Has(byName, "Resident", "Dana Reyes"));

        var byAsset = await Search(s, "HVAC");
        Assert.True(Has(byAsset, "Asset", "Rooftop HVAC 1"));
    }

    [Fact]
    public async Task Matching_is_case_insensitive_and_ranks_substring_hits_first()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var hits = await Search(s, "HARBOR POINT");
        Assert.Equal("Property", hits[0].GetProperty("type").GetString());
        Assert.Contains("Harbor Point", hits[0].GetProperty("label").GetString());
    }

    [Fact]
    public async Task Tolerates_a_spelling_variant_via_trigram_similarity()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var hits = await Search(s, "Harbour Point");
        Assert.True(Has(hits, "Property", "Harbor Point Apartments"));
    }

    [Fact]
    public async Task Search_is_confined_to_the_current_tenant()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var hits = await Search(s, "Other");
        Assert.DoesNotContain(hits, h => h.GetProperty("id").GetGuid() == s.PropertyB);
        Assert.DoesNotContain(hits, h => h.GetProperty("id").GetGuid() == s.VendorB);

        Assert.Empty(await Search(s, "Sam Okafor"));
    }

    [Fact]
    public async Task A_short_query_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.GetAsync("/api/search?q=a")).StatusCode);
    }

    [Fact]
    public async Task The_result_count_is_capped_by_limit()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var hits = await Search(s, "an", limit: 1);
        Assert.True(hits.Length <= 1);
    }

    [Fact]
    public async Task Read_only_role_can_search()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/search?q=harbor")).StatusCode);
    }
}
