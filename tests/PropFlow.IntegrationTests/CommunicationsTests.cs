using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Communications;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class CommunicationsTests(DatabaseFixture fixture)
{
    private static (InMemorySentMessageLog Log, IMessageSender[] Senders) Senders()
    {
        var log = new InMemorySentMessageLog();
        return (log, [new MockSmsSender(log), new MockEmailSender(log)]);
    }

    [Fact]
    public async Task Outbox_delivers_pending_messages_through_the_matching_sender()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var store = s.Comms(s.OrganizationA))
        {
            store.OutboxMessages.Add(new OutboxMessage(s.OrganizationA, Guid.NewGuid(), MessageChannel.Sms,
                "+15550001111", null, "Pest control is on the way.", "work-1-otw", DateTimeOffset.UtcNow));
            store.OutboxMessages.Add(new OutboxMessage(s.OrganizationA, Guid.NewGuid(), MessageChannel.Email,
                "resident@example.test", "Visit scheduled", "Your visit is Friday.", "work-2-sched", DateTimeOffset.UtcNow));
            await store.SaveChangesAsync();
        }

        var (log, senders) = Senders();
        await using (var store = s.Comms(s.OrganizationA))
        {
            var processed = await new OutboxProcessor(store, senders, TimeProvider.System).ProcessPendingAsync(default);
            Assert.Equal(2, processed);
        }

        await using var verify = s.Comms(s.OrganizationA);
        var messages = await verify.OutboxMessages.OrderBy(x => x.IdempotencyKey).ToListAsync();
        Assert.All(messages, m => Assert.Equal(OutboxStatus.Sent, m.Status));
        Assert.All(messages, m => Assert.False(string.IsNullOrWhiteSpace(m.ProviderReference)));
        Assert.Equal(2, log.Sent.Count);
        Assert.Contains(log.Sent, m => m.Channel == MessageChannel.Sms && m.Body == "Pest control is on the way.");
        Assert.Contains(log.Sent, m => m.Channel == MessageChannel.Email && m.Subject == "Visit scheduled");
    }

    [Fact]
    public async Task Enqueue_is_idempotent_by_key()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var submission = new OutboxSubmission(MessageChannel.Sms, "+15550002222", null, "On the way.", "work-9-otw");

        await using (var store = s.Comms(s.OrganizationA))
            Assert.True(await new EfOutbox(store, TimeProvider.System).EnqueueAsync(submission, default));
        await using (var store = s.Comms(s.OrganizationA))
            Assert.False(await new EfOutbox(store, TimeProvider.System).EnqueueAsync(submission, default));

        await using var verify = s.Comms(s.OrganizationA);
        Assert.Single(await verify.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Retried_dispatch_does_not_send_a_message_twice()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var store = s.Comms(s.OrganizationA))
        {
            store.OutboxMessages.Add(new OutboxMessage(s.OrganizationA, Guid.NewGuid(), MessageChannel.Sms,
                "+15550003333", null, "Completed.", "work-3-done", DateTimeOffset.UtcNow));
            await store.SaveChangesAsync();
        }

        var (log, senders) = Senders();
        await using (var store = s.Comms(s.OrganizationA))
            Assert.Equal(1, await new OutboxProcessor(store, senders, TimeProvider.System).ProcessPendingAsync(default));
        await using (var store = s.Comms(s.OrganizationA))
            Assert.Equal(0, await new OutboxProcessor(store, senders, TimeProvider.System).ProcessPendingAsync(default));

        Assert.Single(log.Sent);

        // The sender itself collapses a duplicate delivery for the same idempotency key.
        var outbound = new OutboundMessage(MessageChannel.Sms, "+15550003333", null, "Completed.");
        await senders[0].SendAsync(outbound, "work-3-done", default);
        Assert.Single(log.Sent);
    }

    [Fact]
    public async Task Outbox_is_tenant_isolated()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await using (var store = s.Comms(s.OrganizationA))
        {
            store.OutboxMessages.Add(new OutboxMessage(s.OrganizationA, Guid.NewGuid(), MessageChannel.Sms,
                "+15550004444", null, "Private to A.", "a-only", DateTimeOffset.UtcNow));
            await store.SaveChangesAsync();
        }

        var (_, senders) = Senders();
        await using (var store = s.Comms(s.OrganizationB))
            Assert.Equal(0, await new OutboxProcessor(store, senders, TimeProvider.System).ProcessPendingAsync(default));

        await using (var store = s.Comms(s.OrganizationB))
            Assert.Empty(await store.OutboxMessages.IgnoreQueryFilters().ToListAsync());

        await using var connection = new NpgsqlConnection(fixture.RuntimeConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM communications.\"OutboxMessages\"";
        Assert.Equal(0L, await command.ExecuteScalarAsync());

        await using var verify = s.Comms(s.OrganizationA);
        Assert.Equal(OutboxStatus.Pending, (await verify.OutboxMessages.SingleAsync()).Status);
    }

    [Fact]
    public async Task Template_crud_flows_through_the_api_and_requires_capability()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        var create = await s.Client.PostAsJsonAsync("/api/communication/templates", new
        {
            name = "Pest control scheduled", channel = "Email", subject = "Pest control",
            body = "Scheduled for {{ day }} between {{ window }}."
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var bad = await s.Client.PostAsJsonAsync("/api/communication/templates", new
        {
            name = "No subject", channel = "Email", subject = (string?)null, body = "Body"
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var list = await s.Client.GetFromJsonAsync<JsonElement[]>("/api/communication/templates");
        Assert.Contains(list!, t => t.GetProperty("id").GetGuid() == id);

        var update = await s.Client.PutAsJsonAsync($"/api/communication/templates/{id}", new
        {
            name = "Pest control scheduled", subject = "Updated", body = "New body {{ day }}.", isActive = false
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated", updated.GetProperty("subject").GetString());
        Assert.False(updated.GetProperty("isActive").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.DeleteAsync($"/api/communication/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/communication/templates/{id}")).StatusCode);
    }

    [Fact]
    public async Task Read_only_role_cannot_reach_templates()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/communication/templates")).StatusCode);
    }

    [Fact]
    public async Task Persisted_template_body_renders_against_supplied_values()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var id = Guid.NewGuid();
        await using (var store = s.Comms(s.OrganizationA))
        {
            store.MessageTemplates.Add(new MessageTemplate(s.OrganizationA, id, "On the way",
                MessageChannel.Sms, null, "Your technician is on the way and will arrive by {{ eta }}."));
            await store.SaveChangesAsync();
        }

        await using var verify = s.Comms(s.OrganizationA);
        var template = await verify.MessageTemplates.SingleAsync(x => x.Id == id);
        var rendered = new TemplateRenderer().Render(template.Body,
            new Dictionary<string, string> { ["eta"] = "3:30 PM" });
        Assert.Equal("Your technician is on the way and will arrive by 3:30 PM.", rendered);
    }
}
