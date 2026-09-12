using System.Text.Json;

namespace PropFlow.Domain.Configuration;

// PF-S03.06. One row per organization - a singleton, enforced by a unique index on
// OrganizationId (OperationsStore.cs), not by this type, the same "uniqueness lives in the
// index" shape NumberingSequence uses for its (OrganizationId, AppliesTo) key.
//
// Business hours are storage and validation only: nothing consults them yet (no "closed for
// business" check anywhere in the codebase), the same order templates existed before dispatch
// did (PF-4.03 vs PF-4.05). DefaultTimeZoneId is a fallback for a property that doesn't set its
// own - Property.TimeZoneId (PF-7.04) always wins over this when a property has one.
public sealed class OrganizationSettings : TenantEntity
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private OrganizationSettings(Guid organizationId, Guid id) : base(organizationId, id) { }

    public static OrganizationSettings Create(Guid organizationId, Guid id, string defaultTimeZoneId, DateTimeOffset createdAt) => new(organizationId, id)
    {
        DefaultTimeZoneId = ValidateTimeZone(defaultTimeZoneId),
        BusinessHoursJson = SerializeHours(ClosedAllWeek()),
        CreatedAt = createdAt.ToUniversalTime(),
    };

    public string DefaultTimeZoneId { get; private set; } = "";
    public string BusinessHoursJson { get; private set; } = "[]";
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetDefaultTimeZone(string timeZoneId) => DefaultTimeZoneId = ValidateTimeZone(timeZoneId);

    /// <summary>
    /// Replaces the whole week at once - exactly one window per <see cref="DayOfWeek"/>, in any
    /// order. A window with both Open and Close null means closed that day; either one set alone
    /// is rejected, and a day missing from the list is rejected rather than defaulted, so a
    /// partial update can never silently leave a stale day in place.
    /// </summary>
    public void SetBusinessHours(IReadOnlyList<BusinessHoursWindow> hours) => BusinessHoursJson = SerializeHours(hours);

    public IReadOnlyList<BusinessHoursWindow> ReadBusinessHours() =>
        JsonSerializer.Deserialize<BusinessHoursWindow[]>(BusinessHoursJson, Json) ?? ClosedAllWeek();

    private static IReadOnlyList<BusinessHoursWindow> ClosedAllWeek() =>
        Enum.GetValues<DayOfWeek>().Select(d => new BusinessHoursWindow(d, null, null)).ToArray();

    private static string SerializeHours(IReadOnlyList<BusinessHoursWindow>? hours)
    {
        if (hours is null || hours.Count != 7 || hours.Select(h => h.Day).Distinct().Count() != 7)
            throw new ArgumentException("Business hours must specify exactly one window per day of week.", nameof(hours));
        foreach (var h in hours)
        {
            if (h.Open is null != h.Close is null)
                throw new ArgumentException($"{h.Day}: open and close must both be set, or both left blank for a closed day.", nameof(hours));
            if (h.Open is not null && h.Close is not null && h.Open >= h.Close)
                throw new ArgumentException($"{h.Day}: open time must be before close time.", nameof(hours));
        }
        return JsonSerializer.Serialize(hours.OrderBy(h => h.Day), Json);
    }

    private static string ValidateTimeZone(string? value)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length is 0 or > 100 || !normalized.Contains('/', StringComparison.Ordinal) || normalized.Contains(' ', StringComparison.Ordinal))
            throw new ArgumentException("An IANA time zone is required.", nameof(value));
        return normalized;
    }
}

public sealed record BusinessHoursWindow(DayOfWeek Day, TimeOnly? Open, TimeOnly? Close);
