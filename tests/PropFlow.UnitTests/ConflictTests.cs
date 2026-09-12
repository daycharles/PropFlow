using PropFlow.Domain.Integrations;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ConflictTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Connection = Guid.NewGuid();
    private static readonly Guid Run1 = Guid.NewGuid();
    private static readonly Guid Run2 = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static Conflict New(ConflictReason reason = ConflictReason.UnmappedValue, string field = "Status") =>
        new(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.WorkOrder, "WO-1", reason, field,
            "Awaiting Parts", "InProgress", "No mapping rule covers 'Awaiting Parts'.", Run1, T0);

    [Fact]
    public void A_new_conflict_is_open_and_first_seen_equals_last_seen()
    {
        var conflict = New();

        Assert.True(conflict.IsOpen);
        Assert.Equal(Run1, conflict.FirstSeenInRunId);
        Assert.Equal(Run1, conflict.LastSeenInRunId);
        Assert.Equal(T0, conflict.FirstSeenAt);
        Assert.Equal(T0, conflict.LastSeenAt);
        Assert.Equal(1, conflict.ObservationCount);
        Assert.Null(conflict.ResolvedAt);
    }

    [Fact]
    public void The_key_is_exactly_the_columns_of_the_partial_unique_index()
    {
        var conflict = New();

        Assert.Equal(
            new ConflictKey(Connection, IntegrationEntityKind.WorkOrder, "WO-1", ConflictReason.UnmappedValue, "Status"),
            conflict.Key);
    }

    [Fact]
    public void Two_conflicts_on_the_same_record_for_different_reasons_or_fields_are_different_keys()
    {
        Assert.NotEqual(New().Key, New(ConflictReason.ValidationRefusal).Key);
        Assert.NotEqual(New().Key, New(field: "TimeZone").Key);
    }

    [Fact]
    public void Requires_a_connection_a_run_an_external_id_and_a_field()
    {
        Assert.Throws<ArgumentException>(() => new Conflict(Org, Guid.NewGuid(), Guid.Empty,
            IntegrationEntityKind.Asset, "A-1", ConflictReason.AmbiguousMatch, "Name", null, null, null, Run1, T0));
        Assert.Throws<ArgumentException>(() => new Conflict(Org, Guid.NewGuid(), Connection,
            IntegrationEntityKind.Asset, "A-1", ConflictReason.AmbiguousMatch, "Name", null, null, null, Guid.Empty, T0));
        Assert.Throws<ArgumentException>(() => new Conflict(Org, Guid.NewGuid(), Connection,
            IntegrationEntityKind.Asset, " ", ConflictReason.AmbiguousMatch, "Name", null, null, null, Run1, T0));
        Assert.Throws<ArgumentException>(() => new Conflict(Org, Guid.NewGuid(), Connection,
            IntegrationEntityKind.Asset, "A-1", ConflictReason.AmbiguousMatch, " ", null, null, null, Run1, T0));
    }

    // --- Re-observation is idempotent ---------------------------------------------------------

    [Fact]
    public void Re_observing_in_a_later_run_moves_only_the_last_seen_side()
    {
        var conflict = New();
        conflict.Observe(Run2, "Awaiting Parts", "InProgress", "Still unmapped.", T0.AddDays(1));

        Assert.Equal(Run1, conflict.FirstSeenInRunId);
        Assert.Equal(T0, conflict.FirstSeenAt);
        Assert.Equal(Run2, conflict.LastSeenInRunId);
        Assert.Equal(T0.AddDays(1), conflict.LastSeenAt);
        Assert.Equal(2, conflict.ObservationCount);
        Assert.True(conflict.IsOpen);
    }

    [Fact]
    public void Re_observing_twice_within_one_run_does_not_double_count()
    {
        var conflict = New();
        conflict.Observe(Run2, "Awaiting Parts", "InProgress", null, T0.AddDays(1));
        conflict.Observe(Run2, "Awaiting Parts", "InProgress", null, T0.AddDays(1));

        Assert.Equal(2, conflict.ObservationCount);
    }

    [Fact]
    public void Re_observing_refreshes_the_observed_and_current_values()
    {
        var conflict = New();
        conflict.Observe(Run2, "On Hold", "OnHold", "Vendor renamed the status.", T0.AddDays(1));

        Assert.Equal("On Hold", conflict.ObservedValue);
        Assert.Equal("OnHold", conflict.CurrentValue);
        Assert.Equal("Vendor renamed the status.", conflict.Detail);
    }

    [Fact]
    public void Observing_requires_a_run()
    {
        Assert.Throws<ArgumentException>(() => New().Observe(Guid.Empty, null, null, null, T0));
    }

    // --- Resolution is a transition, never a removal ------------------------------------------

    [Fact]
    public void Resolving_records_the_actor_the_time_and_the_note()
    {
        var conflict = New();
        conflict.Resolve(Actor, T0.AddHours(3), "Added a rule for 'Awaiting Parts'.");

        Assert.Equal(ConflictStatus.Resolved, conflict.Status);
        Assert.False(conflict.IsOpen);
        Assert.Equal(Actor, conflict.ResolvedByUserId);
        Assert.Equal(T0.AddHours(3), conflict.ResolvedAt);
        Assert.Equal("Added a rule for 'Awaiting Parts'.", conflict.ResolutionNote);
    }

    [Fact]
    public void Ignoring_is_the_other_transition_and_is_equally_final()
    {
        var conflict = New();
        conflict.Ignore(Actor, T0, "PropFlow is right, the source is not.");

        Assert.Equal(ConflictStatus.Ignored, conflict.Status);
        Assert.Throws<InvalidOperationException>(() => conflict.Resolve(Actor, T0, null));
    }

    [Fact]
    public void A_closed_conflict_is_not_reopened_by_a_later_sighting()
    {
        var conflict = New();
        conflict.Resolve(Actor, T0.AddHours(1), null);

        // A recurrence is a NEW row; the partial unique index covers Open rows only, so that is
        // legal, and the resolved row survives as audit history.
        Assert.Throws<InvalidOperationException>(() =>
            conflict.Observe(Run2, "Awaiting Parts", "InProgress", null, T0.AddDays(1)));
        Assert.Equal(ConflictStatus.Resolved, conflict.Status);
    }

    [Fact]
    public void Closing_requires_an_actor_and_cannot_happen_twice()
    {
        var conflict = New();
        Assert.Throws<ArgumentException>(() => conflict.Resolve(Guid.Empty, T0, null));

        conflict.Resolve(Actor, T0, null);
        Assert.Throws<InvalidOperationException>(() => conflict.Resolve(Actor, T0, null));
    }

    // --- Value handling -----------------------------------------------------------------------

    [Fact]
    public void An_over_long_or_multi_line_source_value_is_summarised_rather_than_refused()
    {
        var conflict = new Conflict(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.WorkOrder, "WO-2",
            ConflictReason.ValidationRefusal, "Description", new string('x', 900) + "\nsecond line",
            null, null, Run1, T0);

        Assert.NotNull(conflict.ObservedValue);
        Assert.Equal(Conflict.ValueMaxLength, conflict.ObservedValue!.Length);
        Assert.EndsWith("…", conflict.ObservedValue, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', conflict.ObservedValue);
    }

    [Fact]
    public void Blank_values_become_null()
    {
        var conflict = new Conflict(Org, Guid.NewGuid(), Connection, IntegrationEntityKind.Property, "P-1",
            ConflictReason.UpstreamDisappearance, "ExternalId", "   ", null, "", Run1, T0);

        Assert.Null(conflict.ObservedValue);
        Assert.Null(conflict.CurrentValue);
        Assert.Null(conflict.Detail);
    }
}
