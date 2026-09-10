using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Communications;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ResidentMessageTests(DatabaseFixture fixture)
{
    private static (InMemorySentMessageLog Log, IMessageSender[] Senders) Senders()
    {
        var log = new InMemorySentMessageLog();
        return (log, [new MockSmsSender(log), new MockEmailSender(log)]);
    }

    // A work item that has ResidentA (who has SMS consent) as its resident.
    private static async Task<Guid> SeedWorkWithResidentAsync(Scenario s)
    {
        var id = Guid.NewGuid();
        await using var store = s.AdminStore(s.OrganizationA);
        var work = new WorkItem(s.OrganizationA, id, "Quarterly pest treatment", s.PropertyA, s.AdminA);
        work.SetLocation(s.PropertyA, null, null, s.ResidentA);
        work.Publish(DateTimeOffset.UtcNow);
        store.WorkItems.Add(work);
        await store.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedSmsTemplateAsync(Scenario s, string body = "Hi {{ resident.name }}, your {{ work.title }} is coming up.")
    {
        var id = Guid.NewGuid();
        await using var store = s.CommsAsAdmin(s.OrganizationA);
        store.MessageTemplates.Add(new MessageTemplate(s.OrganizationA, id, "Pest reminder", MessageChannel.Sms, null, body));
        await store.SaveChangesAsync();
        return id;
    }

    private static async Task<JsonElement[]> TimelineAsync(Scenario s, Guid workId) =>
        (await s.Client.GetFromJsonAsync<JsonElement[]>($"/api/work/{workId}/timeline"))!;

    [Fact]
    public async Task A_queued_message_shows_on_the_work_timeline_then_flips_to_sent()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var workId = await SeedWorkWithResidentAsync(s);
        var templateId = await SeedSmsTemplateAsync(s);

        var response = await s.Client.PostAsJsonAsync($"/api/work/{workId}/message", new { templateId });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var queued = Assert.Single(await TimelineAsync(s, workId), e => e.GetProperty("eventType").GetString() == "MessageQueued");
        Assert.True(queued.GetProperty("residentVisible").GetBoolean());
        Assert.Contains("Sms", queued.GetProperty("newValue").GetString());

        // the enqueued body was rendered against the work + resident
        await using (var comms = s.Comms(s.OrganizationA))
        {
            var msg = await comms.OutboxMessages.SingleAsync(x => x.WorkId == workId);
            Assert.Equal("Hi Dana Reyes, your Quarterly pest treatment is coming up.", msg.Body);
            Assert.Equal("+15550100101", msg.RecipientAddress);
            Assert.True(msg.ResidentVisible);
        }

        // run the dispatcher; the timeline entry now reads MessageSent
        var (_, senders) = Senders();
        await using (var comms = s.Comms(s.OrganizationA))
            Assert.Equal(1, await new OutboxProcessor(comms, senders, TimeProvider.System, new() { RetryDelay = TimeSpan.Zero })
                .ProcessPendingAsync(default));

        Assert.Contains(await TimelineAsync(s, workId), e => e.GetProperty("eventType").GetString() == "MessageSent");
        Assert.DoesNotContain(await TimelineAsync(s, workId), e => e.GetProperty("eventType").GetString() == "MessageQueued");
    }

    [Fact]
    public async Task Sending_is_refused_without_consent_a_resident_or_a_template()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var withResident = await SeedWorkWithResidentAsync(s);
        var templateId = await SeedSmsTemplateAsync(s);

        // ResidentA has SMS consent but not email — an email template is refused
        var emailTemplate = Guid.NewGuid();
        await using (var store = s.CommsAsAdmin(s.OrganizationA))
        {
            store.MessageTemplates.Add(new MessageTemplate(s.OrganizationA, emailTemplate, "Email note", MessageChannel.Email, "Update", "Hello {{ resident.name }}"));
            await store.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await s.Client.PostAsJsonAsync($"/api/work/{withResident}/message", new { templateId = emailTemplate })).StatusCode);

        // WorkA has no resident
        Assert.Equal(HttpStatusCode.Conflict,
            (await s.Client.PostAsJsonAsync($"/api/work/{s.WorkA}/message", new { templateId })).StatusCode);

        // unknown template
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.Client.PostAsJsonAsync($"/api/work/{withResident}/message", new { templateId = Guid.NewGuid() })).StatusCode);

        // unknown work
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.Client.PostAsJsonAsync($"/api/work/{Guid.NewGuid()}/message", new { templateId })).StatusCode);
    }

    [Fact]
    public async Task A_double_submit_of_the_same_template_is_deduped()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var workId = await SeedWorkWithResidentAsync(s);
        var templateId = await SeedSmsTemplateAsync(s);

        var first = await (await s.Client.PostAsJsonAsync($"/api/work/{workId}/message", new { templateId }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await s.Client.PostAsJsonAsync($"/api/work/{workId}/message", new { templateId }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(first.GetProperty("queued").GetBoolean());
        Assert.False(second.GetProperty("queued").GetBoolean());

        await using var comms = s.Comms(s.OrganizationA);
        Assert.Equal(1, await comms.OutboxMessages.CountAsync(x => x.WorkId == workId));
    }

    [Fact]
    public async Task A_control_character_reaching_a_rendered_email_subject_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var workId = await SeedWorkWithResidentAsync(s);
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            // grant ResidentA email consent so an email template is deliverable
            var r = await store.Residents.SingleAsync(x => x.Id == s.ResidentA);
            r.SetConsent(PropFlow.Domain.Communications.MessageChannel.Email, granted: true, DateTimeOffset.UtcNow);
            // a work title carrying a CRLF, which flows into {{ work.title }}
            var w = await store.WorkItems.SingleAsync(x => x.Id == workId);
            w.Edit("Roof repair\r\nBcc: attacker@evil.test", null, null, WorkPriority.Normal);
            await store.SaveChangesAsync();
        }
        var templateId = Guid.NewGuid();
        await using (var store = s.CommsAsAdmin(s.OrganizationA))
        {
            store.MessageTemplates.Add(new MessageTemplate(s.OrganizationA, templateId, "Injected",
                MessageChannel.Email, "Re: {{ work.title }}", "Hello {{ resident.name }}"));
            await store.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            (await s.Client.PostAsJsonAsync($"/api/work/{workId}/message", new { templateId })).StatusCode);

        await using var comms = s.Comms(s.OrganizationA);
        Assert.Equal(0, await comms.OutboxMessages.CountAsync(x => x.WorkId == workId));
    }

    [Fact]
    public async Task A_scheduled_visit_renders_in_the_property_time_zone()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        // 14:00 UTC on 2026-07-01 is 10:00 in America/New_York (EDT), the PropertyA zone.
        var start = new DateTimeOffset(2026, 7, 1, 14, 0, 0, TimeSpan.Zero);
        var workId = Guid.NewGuid();
        await using (var store = s.AdminStore(s.OrganizationA))
        {
            var work = new WorkItem(s.OrganizationA, workId, "Quarterly pest treatment", s.PropertyA, s.AdminA);
            work.SetLocation(s.PropertyA, null, null, s.ResidentA);
            work.Publish(DateTimeOffset.UtcNow);
            work.AssignVendor(s.VendorA);
            work.Schedule(start, start.AddHours(2));
            store.WorkItems.Add(work);
            await store.SaveChangesAsync();
        }
        var templateId = await SeedSmsTemplateAsync(s, "Your visit is at {{ schedule.start }}.");

        Assert.Equal(HttpStatusCode.Accepted,
            (await s.Client.PostAsJsonAsync($"/api/work/{workId}/message", new { templateId })).StatusCode);

        var expected = TimeZoneInfo.ConvertTime(start, TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).ToString("f");
        await using var comms = s.Comms(s.OrganizationA);
        var message = await comms.OutboxMessages.SingleAsync(x => x.WorkId == workId);
        Assert.Equal($"Your visit is at {expected}.", message.Body);
        Assert.Contains("10:00", message.Body);
    }

    [Fact]
    public async Task A_template_placeholder_with_no_value_is_a_400()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var workId = await SeedWorkWithResidentAsync(s);
        var templateId = await SeedSmsTemplateAsync(s, "Hi {{ resident.nickname }}");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await s.Client.PostAsJsonAsync($"/api/work/{workId}/message", new { templateId })).StatusCode);
    }

    [Fact]
    public async Task Read_only_role_cannot_send()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var workId = await SeedWorkWithResidentAsync(s);
        var templateId = await SeedSmsTemplateAsync(s);
        await s.LoginAsync(reader: true);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Client.PostAsJsonAsync($"/api/work/{workId}/message", new { templateId })).StatusCode);
    }

    [Fact]
    public async Task A_resident_message_never_crosses_a_tenant_boundary()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var templateId = await SeedSmsTemplateAsync(s);

        // org B's work item is invisible to org A's session
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.Client.PostAsJsonAsync($"/api/work/{s.WorkB}/message", new { templateId })).StatusCode);
    }
}
