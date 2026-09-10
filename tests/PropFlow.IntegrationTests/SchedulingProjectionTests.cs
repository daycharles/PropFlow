using System.Net.Http.Json;
using System.Text.Json;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class SchedulingProjectionTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Work_detail_keeps_utc_instants_and_exposes_property_local_schedule()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var workId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 7, 1, 14, 0, 0, TimeSpan.Zero);
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var work = new WorkItem(s.OrganizationA, workId, "Scheduled HVAC visit", s.PropertyA, s.AdminA);
            work.Publish(start.AddDays(-1));
            work.AssignVendor(s.VendorA);
            work.Schedule(start, start.AddHours(1));
            store.WorkItems.Add(work);
            await store.SaveChangesAsync();
        }

        await s.LoginAsync();
        var response = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{workId}");
        var item = response.GetProperty("item");
        Assert.Equal(start, item.GetProperty("scheduledStart").GetDateTimeOffset());
        Assert.Equal("America/New_York", response.GetProperty("propertyTimeZone").GetString());
        var localStart = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.FromHours(-4));
        Assert.Equal(localStart, response.GetProperty("scheduledStartLocal").GetDateTimeOffset());
        Assert.Equal(localStart.AddHours(1), response.GetProperty("scheduledEndLocal").GetDateTimeOffset());
    }
}
