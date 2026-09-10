using PropFlow.Domain.Work;

namespace PropFlow.Application.Work;

/// <summary>
/// Flat read model for the work list. Projected in a single query so the list does not
/// issue one version/lookup round trip per row.
/// </summary>
public sealed record WorkListItem(
    Guid Id,
    string Title,
    string? Description,
    WorkStatus Status,
    WorkPriority Priority,
    WorkType WorkType,
    Guid PropertyId,
    string? PropertyName,
    Guid? BuildingId,
    Guid? SpaceId,
    Guid? CategoryId,
    string? CategoryName,
    Guid? VendorId,
    string? VendorName,
    Guid? EmployeeId,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt,
    uint Version);
