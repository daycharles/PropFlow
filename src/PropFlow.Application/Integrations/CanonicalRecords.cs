namespace PropFlow.Application.Integrations;

// The vendor-neutral shapes an integration adapter produces. They are a projection of what an
// external property-management system exposes, not PropFlow domain entities: every record
// carries the source `ExternalId`, references parents by their external ids, and holds only
// the fields the milestone-6 reconciliation will need. Adapters map their proprietary payloads
// onto these; nothing downstream sees a Yardi or AppFolio type.

public sealed record CanonicalProperty(
    string ExternalId,
    string Name,
    string? AddressLine,
    string? City,
    string? Region,
    string? PostalCode,
    string? TimeZone);

public sealed record CanonicalSpace(
    string ExternalId,
    string PropertyExternalId,
    string Code,
    string? BuildingExternalId);

public sealed record CanonicalOccupancy(
    string ExternalId,
    string SpaceExternalId,
    string ResidentName,
    string? Email,
    string? Phone,
    DateOnly MovedInOn,
    DateOnly? MovedOutOn);

public sealed record CanonicalWorkOrder(
    string ExternalId,
    string PropertyExternalId,
    string? SpaceExternalId,
    string Title,
    string? Description,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt);

public sealed record CanonicalAsset(
    string ExternalId,
    string PropertyExternalId,
    string? SpaceExternalId,
    string Name,
    string? Kind,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    DateOnly? InstalledOn);

// One full pull from a source system: every record it currently knows about, by kind.
public sealed record IntegrationSnapshot(
    string SourceSystem,
    IReadOnlyList<CanonicalProperty> Properties,
    IReadOnlyList<CanonicalSpace> Spaces,
    IReadOnlyList<CanonicalOccupancy> Occupancies,
    IReadOnlyList<CanonicalWorkOrder> WorkOrders,
    IReadOnlyList<CanonicalAsset> Assets)
{
    public static IntegrationSnapshot Empty(string sourceSystem) => new(sourceSystem, [], [], [], [], []);
}
