namespace PropFlow.Domain.Reporting;

public enum ReportScheduleFrequency { Daily, Weekly, Monthly }
public enum ReportExportFormat { Json, Csv }

public sealed class ReportSchedule(Guid organizationId, Guid id, string name, string kind, ReportScheduleFrequency frequency,
    DateTimeOffset nextRunAt, string recipient, ReportExportFormat format = ReportExportFormat.Csv, string? filterJson = null)
    : TenantEntity(organizationId, id)
{
    public string Name { get; private set; } = Required(name, nameof(name), 120);
    public string Kind { get; private set; } = Required(kind, nameof(kind), 40).ToLowerInvariant();
    public ReportScheduleFrequency Frequency { get; private set; } = frequency;
    public DateTimeOffset NextRunAt { get; private set; } = nextRunAt;
    public string Recipient { get; private set; } = Required(recipient, nameof(recipient), 254);
    public ReportExportFormat Format { get; private set; } = format;
    public string? FilterJson { get; private set; } = filterJson;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? LastRunAt { get; private set; }
    public int DeliveryCount { get; private set; }

    public void MarkDelivered(DateTimeOffset deliveredAt)
    {
        LastRunAt = deliveredAt; DeliveryCount++; NextRunAt = Frequency switch
        {
            ReportScheduleFrequency.Daily => deliveredAt.AddDays(1),
            ReportScheduleFrequency.Weekly => deliveredAt.AddDays(7),
            _ => deliveredAt.AddMonths(1)
        };
    }
    public void Pause() => IsActive = false;
    public void Resume(DateTimeOffset nextRunAt) { IsActive = true; NextRunAt = nextRunAt; }
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim() is var v && v.Length <= max ? v : throw new ArgumentException($"Value may contain at most {max} characters.", name);
}

public sealed class ReportDelivery(Guid organizationId, Guid id, Guid scheduleId, DateTimeOffset deliveredAt, string payloadHash, int rowCount)
    : TenantEntity(organizationId, id)
{
    public Guid ScheduleId { get; private set; } = scheduleId == Guid.Empty ? throw new ArgumentException("Schedule is required.", nameof(scheduleId)) : scheduleId;
    public DateTimeOffset DeliveredAt { get; private set; } = deliveredAt;
    public string PayloadHash { get; private set; } = string.IsNullOrWhiteSpace(payloadHash) ? throw new ArgumentException("Payload hash is required.", nameof(payloadHash)) : payloadHash;
    public int RowCount { get; private set; } = rowCount >= 0 ? rowCount : throw new ArgumentOutOfRangeException(nameof(rowCount));
}
