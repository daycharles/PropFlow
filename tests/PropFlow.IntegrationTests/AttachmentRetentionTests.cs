using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Application.Attachments;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Attachments;
using Xunit;

namespace PropFlow.IntegrationTests;

// PF-7.01: retention is enforced, not just recorded. The sweep deletes an attachment whose
// RetainUntil has passed — the row and its blob — and leaves everything else alone.
[Collection("PostgreSQL")]
public sealed class AttachmentRetentionTests(DatabaseFixture fixture)
{
    private sealed class RecordingStorage : IAttachmentStorage
    {
        public ConcurrentDictionary<string, byte[]> Blobs { get; } = new();
        public Task StoreAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            Blobs[storageKey] = buffer.ToArray();
            return Task.CompletedTask;
        }
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(Blobs.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            Blobs.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task The_sweep_deletes_expired_attachments_and_their_blobs_but_keeps_the_rest()
    {
        await using var s = await fixture.CreateScenarioAsync();
        var now = DateTimeOffset.UtcNow;
        var expiredKey = $"{s.OrganizationA:N}/expired";
        var liveKey = $"{s.OrganizationA:N}/live";
        var openEndedKey = $"{s.OrganizationA:N}/open";
        var otherTenantExpiredKey = $"{s.OrganizationB:N}/expired";

        var storage = new RecordingStorage();
        storage.Blobs[expiredKey] = [1];
        storage.Blobs[liveKey] = [2];
        storage.Blobs[openEndedKey] = [3];
        storage.Blobs[otherTenantExpiredKey] = [4];

        var expiredId = Guid.NewGuid();
        await using (var a = s.AdminStore(s.OrganizationA))
        {
            a.Attachments.Add(Attachment.Create(s.OrganizationA, expiredId, s.WorkA, "old.pdf", "application/pdf", 1,
                expiredKey, false, now.AddDays(-30), now.AddDays(-1)));
            a.Attachments.Add(Attachment.Create(s.OrganizationA, Guid.NewGuid(), s.WorkA, "soon.pdf", "application/pdf", 1,
                liveKey, false, now.AddDays(-30), now.AddDays(1)));
            a.Attachments.Add(Attachment.Create(s.OrganizationA, Guid.NewGuid(), s.WorkA, "forever.pdf", "application/pdf", 1,
                openEndedKey, false, now.AddDays(-30), null));
            await a.SaveChangesAsync();
        }
        await using (var b = s.AdminStore(s.OrganizationB))
        {
            b.Attachments.Add(Attachment.Create(s.OrganizationB, Guid.NewGuid(), s.WorkB, "old-b.pdf", "application/pdf", 1,
                otherTenantExpiredKey, false, now.AddDays(-30), now.AddDays(-1)));
            await b.SaveChangesAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Database"] = fixture.RuntimeConnection })
            .Build();
        var sweep = new AttachmentRetentionSweep(configuration, storage, TimeProvider.System, NullLogger<AttachmentRetentionSweep>.Instance);

        var removed = await sweep.SweepAsync(default);

        Assert.Equal(2, removed); // one per tenant
        await using var verifyA = s.AdminStore(s.OrganizationA);
        Assert.False(await verifyA.Attachments.AnyAsync(x => x.Id == expiredId));
        Assert.Equal(2, await verifyA.Attachments.CountAsync(x => x.WorkId == s.WorkA));
        await using var verifyB = s.AdminStore(s.OrganizationB);
        Assert.Equal(0, await verifyB.Attachments.CountAsync());

        Assert.False(storage.Blobs.ContainsKey(expiredKey));
        Assert.False(storage.Blobs.ContainsKey(otherTenantExpiredKey));
        Assert.True(storage.Blobs.ContainsKey(liveKey));
        Assert.True(storage.Blobs.ContainsKey(openEndedKey));
    }
}
