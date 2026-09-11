namespace PropFlow.Domain.Assets;

public enum MeterUnit
{
    Hours = 1,
    Cycles = 2,
    Miles = 3,
    KilowattHours = 4,
    Gallons = 5,
    Other = 99
}

public sealed class MeterReading : TenantEntity
{
    private MeterReading(Guid organizationId, Guid id) : base(organizationId, id) { }

    public MeterReading(Guid organizationId, Guid id, Guid assetId, string meterName,
        MeterUnit unit, decimal reading, DateOnly readOn)
        : base(organizationId, id)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("Asset is required.", nameof(assetId));
        AssetId = assetId;
        MeterName = Required(meterName, nameof(meterName));
        Unit = unit;
        Reading = NonNegative(reading);
        ReadOn = readOn;
    }

    public Guid AssetId { get; private set; }
    public string MeterName { get; private set; } = "";
    public MeterUnit Unit { get; private set; }
    public decimal Reading { get; private set; }
    public DateOnly ReadOn { get; private set; }

    public bool IsAtLeast(MeterReading prior) =>
        prior.AssetId == AssetId && prior.MeterName.Equals(MeterName, StringComparison.OrdinalIgnoreCase) &&
        prior.Unit == Unit && Reading >= prior.Reading;

    private static decimal NonNegative(decimal value) => value < 0m
        ? throw new ArgumentOutOfRangeException(nameof(value), "Meter reading cannot be negative.") : value;

    private static string Required(string value, string parameter) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 100
            ? value.Trim()
            : throw new ArgumentException("Meter name must contain 1 to 100 characters.", parameter);
}
