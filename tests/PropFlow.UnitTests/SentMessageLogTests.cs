using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class SentMessageLogTests
{
    private static SentMessage Message(string key) =>
        new(MessageChannel.Sms, "+15550001111", null, "Body", key, "ref-" + key);

    [Fact]
    public void Records_a_message_and_exposes_it()
    {
        var log = new InMemorySentMessageLog();

        log.Record(Message("a"));

        Assert.Single(log.Sent);
        Assert.True(log.TryGet("a", out var found));
        Assert.Equal("ref-a", found.ProviderReference);
    }

    [Fact]
    public void Ignores_a_repeat_for_the_same_idempotency_key()
    {
        var log = new InMemorySentMessageLog();

        log.Record(Message("a"));
        log.Record(new SentMessage(MessageChannel.Sms, "+15550009999", null, "Different", "a", "ref-other"));

        Assert.Single(log.Sent);
        Assert.True(log.TryGet("a", out var found));
        Assert.Equal("ref-a", found.ProviderReference);
    }

    [Fact]
    public void TryGet_returns_false_for_an_unknown_key()
    {
        var log = new InMemorySentMessageLog();

        Assert.False(log.TryGet("missing", out _));
    }

    [Fact]
    public void Evicts_the_oldest_entry_past_capacity_and_frees_its_key()
    {
        var log = new InMemorySentMessageLog();
        for (var i = 0; i < 600; i++)
            log.Record(Message($"key-{i:D4}"));

        Assert.Equal(500, log.Sent.Count);
        Assert.False(log.TryGet("key-0000", out _));
        Assert.True(log.TryGet("key-0599", out _));

        // The evicted key is free to record again.
        log.Record(Message("key-0000"));
        Assert.True(log.TryGet("key-0000", out _));
    }

    [Fact]
    public async Task Concurrent_records_do_not_corrupt_the_log()
    {
        var log = new InMemorySentMessageLog();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i =>
            Task.Run(() => log.Record(Message($"c-{i:D3}")))));

        Assert.Equal(200, log.Sent.Count);
    }
}
