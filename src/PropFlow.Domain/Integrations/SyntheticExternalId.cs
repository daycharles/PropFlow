namespace PropFlow.Domain.Integrations;

// External ids PropFlow invents because the canonical record does not carry one.
//
// The only case today is the resident behind an occupancy. CanonicalOccupancy carries the
// resident's name, email and phone but no resident external id (CanonicalRecords.cs:24-31),
// while Occupancy's constructor requires a non-empty ResidentId (People/Occupancy.cs:11-17). The
// reconciler therefore needs a stable key for "the resident of external occupancy X" so a second
// run finds the resident it created on the first run instead of creating another.
//
// The key is derived, not random, so it survives a crash between the two writes: replaying the
// run recomputes the same id and re-finds the same link. The construction lives HERE and nowhere
// else — a string concatenated at a call site is how two call sites end up disagreeing about the
// separator and silently splitting one resident into two.
//
// This is a stand-in, not a design. When an adapter contract gains a real CanonicalResident with
// its own external id, that id supersedes this one for new records: existing links keep their
// synthetic ids and keep resolving, because a link is keyed on (connection, kind, external id)
// and nothing rewrites the stored value. PF-S19.07 documents this in the sandbox contract.
public static class SyntheticExternalId
{
    // '#' cannot appear in the middle of a vendor id by accident often enough to matter, and it
    // is not a separator any of the canonical shapes use.
    public const string ResidentSuffix = "#resident";

    // The longest occupancy id that still yields a storable resident id.
    public static int MaxSourceLength => ExternalRecordLink.ExternalIdMaxLength - ResidentSuffix.Length;

    public static string ForResident(string occupancyExternalId)
    {
        var occupancy = IntegrationConnection.RequireSingleLine(
            occupancyExternalId, nameof(occupancyExternalId), MaxSourceLength);
        return occupancy + ResidentSuffix;
    }

    public static bool IsSyntheticResident(string? externalId) =>
        externalId is not null
        && externalId.EndsWith(ResidentSuffix, StringComparison.Ordinal)
        && externalId.Length > ResidentSuffix.Length;

    // The occupancy id a synthetic resident id was derived from; null when it was not synthetic.
    public static string? OccupancyOf(string? residentExternalId) =>
        IsSyntheticResident(residentExternalId)
            ? residentExternalId![..^ResidentSuffix.Length]
            : null;
}
