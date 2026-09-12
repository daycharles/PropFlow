namespace PropFlow.Domain.Integrations;

// Why the reconciler could not settle a record on its own. Derived from what the reconciler will
// actually raise, not from a general taxonomy:
//
//   UnmappedValue          - an external value has no MappingRule. The canonical case is
//                            CanonicalWorkOrder.Status against the closed WorkStatus enum; there
//                            is deliberately no fallback member, so this is a conflict.
//   MissingRequiredMapping - the MappingProfile lacks a fact a domain constructor demands:
//                            TargetPortfolioId, DefaultCreatorId or a usable time zone.
//   MissingParentLink      - the record references a parent external id that has no link yet, e.g.
//                            a CanonicalSpace whose PropertyExternalId was never seen.
//   UpstreamDisappearance  - a previously-linked record is absent from this snapshot. Never an
//                            automatic delete: the link is retired and a human decides.
//   AmbiguousMatch         - the external record matches more than one candidate PropFlow row.
//   ValidationRefusal      - a domain constructor or method threw on the mapped values.
public enum ConflictReason
{
    UnmappedValue = 0,
    MissingRequiredMapping = 1,
    MissingParentLink = 2,
    UpstreamDisappearance = 3,
    AmbiguousMatch = 4,
    ValidationRefusal = 5
}

public enum ConflictStatus
{
    Open = 0,
    Resolved = 1,
    Ignored = 2
}

// The identity of one divergence. Exactly the columns of the partial unique index PF-S19.04
// creates, so the reconciler's in-memory lookup and the database constraint cannot drift apart.
public sealed record ConflictKey(
    Guid ConnectionId,
    IntegrationEntityKind Kind,
    string ExternalId,
    ConflictReason Reason,
    string Field);

// One divergence the reconciler could not settle, queued for a human.
//
// RE-OBSERVATION IS IDEMPOTENT, AND AN INDEX ENFORCES IT. PF-S19.04 puts
//
//     UNIQUE (OrganizationId, ConnectionId, Kind, ExternalId, Reason, Field) WHERE "Status" = 'Open'
//
// on this table. A second run that re-detects the same divergence must call Observe() on the
// existing OPEN row - which moves LastSeenInRunId and LastSeenAt and leaves FirstSeenInRunId
// alone - rather than inserting a second row. Without that, the conflict queue grows by one row
// per run per divergence and "replay-safe sync" and "conflict queue" contradict each other: a
// nightly sync against an unmapped status would produce 365 identical rows a year.
//
// RESOLUTION IS A STATUS TRANSITION, NEVER A REMOVAL. The runtime role gets SELECT, INSERT and
// UPDATE on this table and no DELETE - the same rung as PaymentRefunds - because a resolved
// conflict is an audit fact: it records that a human looked at a divergence and made a call. A
// divergence that RECURS after resolution is a NEW row, which is legal precisely because the
// index excludes non-Open rows; the resolved row stays as history.
public sealed class Conflict : TenantEntity
{
    public const int FieldMaxLength = 100;
    public const int ValueMaxLength = 400;
    public const int DetailMaxLength = 1000;
    public const int NoteMaxLength = 1000;

    // EF materialization.
    private Conflict(Guid organizationId, Guid id) : base(organizationId, id) { }

    public Conflict(Guid organizationId, Guid id, Guid connectionId, IntegrationEntityKind kind,
        string externalId, ConflictReason reason, string field, string? observedValue, string? currentValue,
        string? detail, Guid runId, DateTimeOffset firstSeenAt) : base(organizationId, id)
    {
        if (connectionId == Guid.Empty) throw new ArgumentException("Connection is required.", nameof(connectionId));
        if (runId == Guid.Empty) throw new ArgumentException("Run is required.", nameof(runId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));

        ConnectionId = connectionId;
        Kind = kind;
        ExternalId = IntegrationConnection.RequireSingleLine(
            externalId, nameof(externalId), ExternalRecordLink.ExternalIdMaxLength);
        Reason = reason;
        Field = IntegrationConnection.RequireSingleLine(field, nameof(field), FieldMaxLength);
        ObservedValue = Summarize(observedValue, ValueMaxLength);
        CurrentValue = Summarize(currentValue, ValueMaxLength);
        Detail = Summarize(detail, DetailMaxLength);

        Status = ConflictStatus.Open;
        FirstSeenInRunId = runId;
        LastSeenInRunId = runId;
        FirstSeenAt = firstSeenAt.ToUniversalTime();
        LastSeenAt = FirstSeenAt;
        ObservationCount = 1;
    }

    public Guid ConnectionId { get; private set; }
    public IntegrationEntityKind Kind { get; private set; }
    public string ExternalId { get; private set; } = "";
    public ConflictReason Reason { get; private set; }

    // The canonical or domain field the divergence is about ("Status", "TimeZone", "PortfolioId").
    // Part of the identity, so two divergences on the same record for different fields are two
    // queue entries rather than one that keeps overwriting itself.
    public string Field { get; private set; } = "";

    // What the source system says, and what PropFlow currently holds. Both nullable: an upstream
    // disappearance has no observed value, and a record PropFlow has never seen has no current one.
    public string? ObservedValue { get; private set; }
    public string? CurrentValue { get; private set; }

    // The sentence a human reads in the queue. Reason is what code branches on.
    public string? Detail { get; private set; }

    public ConflictStatus Status { get; private set; }
    public Guid FirstSeenInRunId { get; private set; }
    public Guid LastSeenInRunId { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    // How many distinct runs have seen this divergence. Re-observing within one run does not
    // increment it, so the number answers "how long has this been broken", not "how chatty is the
    // reconciler".
    public int ObservationCount { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolutionNote { get; private set; }

    public bool IsOpen => Status == ConflictStatus.Open;

    public ConflictKey Key => new(ConnectionId, Kind, ExternalId, Reason, Field);

    /// <summary>
    /// A later run re-detected the same divergence. Updates the "last seen" side and refreshes the
    /// observed and current values; FirstSeenInRunId and FirstSeenAt never move. Refuses on a
    /// closed row: a recurrence after resolution is a new Conflict, which the partial unique index
    /// permits because it only covers Open rows.
    /// </summary>
    public void Observe(Guid runId, string? observedValue, string? currentValue, string? detail,
        DateTimeOffset at)
    {
        if (runId == Guid.Empty) throw new ArgumentException("Run is required.", nameof(runId));
        if (!IsOpen)
            throw new InvalidOperationException(
                $"The conflict is {Status}; a recurrence is recorded as a new conflict, not by reopening this one.");

        if (runId != LastSeenInRunId) ObservationCount++;
        LastSeenInRunId = runId;
        var utc = at.ToUniversalTime();
        if (utc > LastSeenAt) LastSeenAt = utc;
        ObservedValue = Summarize(observedValue, ValueMaxLength);
        CurrentValue = Summarize(currentValue, ValueMaxLength);
        Detail = Summarize(detail, DetailMaxLength);
    }

    /// <summary>A human settled it. A status transition - there is no delete path.</summary>
    public void Resolve(Guid actorId, DateTimeOffset at, string? note) =>
        Close(ConflictStatus.Resolved, actorId, at, note);

    /// <summary>A human decided PropFlow should keep diverging from the source. Also a transition.</summary>
    public void Ignore(Guid actorId, DateTimeOffset at, string? note) =>
        Close(ConflictStatus.Ignored, actorId, at, note);

    private void Close(ConflictStatus status, Guid actorId, DateTimeOffset at, string? note)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        if (!IsOpen) throw new InvalidOperationException($"The conflict is already {Status}.");
        Status = status;
        ResolvedByUserId = actorId;
        ResolvedAt = at.ToUniversalTime();
        ResolutionNote = Summarize(note, NoteMaxLength);
    }

    // Conflict values come from whatever the source system emitted, so they are summarised rather
    // than validated: a 5000-character description or a newline in a vendor field must not make a
    // conflict impossible to record. Control characters are folded to spaces and the result is
    // truncated with an ellipsis. Blank becomes null.
    private static string? Summarize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i])) chars[i] = ' ';
        var flattened = new string(chars).Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..(maxLength - 1)] + "…";
    }
}
