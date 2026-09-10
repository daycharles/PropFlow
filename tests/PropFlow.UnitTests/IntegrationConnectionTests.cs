using PropFlow.Domain.Integrations;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class IntegrationConnectionTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static IntegrationConnection New(string source = "Mock", string name = "Mock system") =>
        new(Org, Guid.NewGuid(), source, name);

    [Fact]
    public void Source_system_is_lower_cased_and_trimmed()
    {
        Assert.Equal("yardi", New(source: "  Yardi  ").SourceSystem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("has space")]
    [InlineData("tab\tin")]
    public void Source_system_rejects_blank_or_whitespace(string source)
    {
        Assert.Throws<ArgumentException>(() => New(source: source));
    }

    [Fact]
    public void Display_name_rejects_control_characters()
    {
        Assert.Throws<ArgumentException>(() => New(name: "Mock" + (char)10 + "system"));
    }

    [Fact]
    public void A_new_connection_is_enabled_with_no_sync_history()
    {
        var connection = New();
        Assert.True(connection.IsEnabled);
        Assert.Null(connection.LastAttemptedAt);
        Assert.Null(connection.LastSucceededAt);
        Assert.Equal(0, connection.ConsecutiveFailures);
    }

    [Fact]
    public void Begin_sync_on_a_disabled_connection_throws()
    {
        var connection = New();
        connection.Disable();
        Assert.Throws<InvalidOperationException>(() => connection.BeginSync(T0));
    }

    [Fact]
    public void A_failed_sync_records_the_error_and_counts_up()
    {
        var connection = New();
        connection.BeginSync(T0);
        connection.FailSync(T0.AddMinutes(1), "adapter timed out");
        connection.FailSync(T0.AddMinutes(2), "adapter timed out again");

        Assert.Equal(2, connection.ConsecutiveFailures);
        Assert.Equal("adapter timed out again", connection.LastError);
        Assert.Equal(T0.AddMinutes(2), connection.LastAttemptedAt);
        Assert.Null(connection.LastSucceededAt);
    }

    [Fact]
    public void A_successful_sync_clears_the_failure_streak()
    {
        var connection = New();
        connection.FailSync(T0, "boom");
        connection.CompleteSync(T0.AddMinutes(5));

        Assert.Equal(0, connection.ConsecutiveFailures);
        Assert.Null(connection.LastError);
        Assert.Equal(T0.AddMinutes(5), connection.LastSucceededAt);
        Assert.Equal(T0.AddMinutes(5), connection.LastAttemptedAt);
    }
}
