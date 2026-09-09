using PropFlow.Domain.Communications;

namespace PropFlow.Domain.Assets;

public enum AssetKind
{
    Other = 0,
    Hvac = 1,
    WaterHeater = 2,
    Appliance = 3,
    Roof = 4,
    ElectricalPanel = 5,
    PlumbingFixture = 6,
    Generator = 7
}

public enum AssetCondition
{
    Unknown = 0,
    New = 1,
    Good = 2,
    Fair = 3,
    Poor = 4,
    EndOfLife = 5
}

// A tracked piece of equipment at a property (and optionally a specific space). Work items
// link to it so an asset accumulates a maintenance history and repeat repairs can be detected.
public sealed class Asset : TenantEntity
{
    // EF materialization only.
    private Asset(Guid organizationId, Guid id) : base(organizationId, id) { }

    public Asset(Guid organizationId, Guid id, Guid propertyId, Guid? spaceId, AssetKind kind, string name)
        : base(organizationId, id)
    {
        PropertyId = propertyId != Guid.Empty ? propertyId : throw new ArgumentException("Property is required.", nameof(propertyId));
        SpaceId = spaceId;
        Kind = kind;
        Name = MessageText.RequireSingleLine(name, nameof(name), 200);
    }

    public Guid PropertyId { get; private set; }
    public Guid? SpaceId { get; private set; }
    public AssetKind Kind { get; private set; }
    public string Name { get; private set; } = "";
    public string? Manufacturer { get; private set; }
    public string? Model { get; private set; }
    public string? SerialNumber { get; private set; }
    public DateOnly? InstalledOn { get; private set; }
    public DateOnly? WarrantyExpiresOn { get; private set; }
    public int? ExpectedServiceLifeYears { get; private set; }
    public AssetCondition Condition { get; private set; }
    public decimal? ReplacementCostEstimate { get; private set; }
    public string? Notes { get; private set; }

    public void Describe(string name, AssetKind kind, string? manufacturer, string? model, string? serialNumber)
    {
        Name = MessageText.RequireSingleLine(name, nameof(name), 200);
        Kind = kind;
        Manufacturer = Optional(manufacturer, nameof(manufacturer), 200);
        Model = Optional(model, nameof(model), 200);
        SerialNumber = Optional(serialNumber, nameof(serialNumber), 200);
    }

    public void SetLifecycle(DateOnly? installedOn, DateOnly? warrantyExpiresOn, int? expectedServiceLifeYears)
    {
        if (expectedServiceLifeYears is < 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(expectedServiceLifeYears), "Expected service life must be 0 to 200 years.");
        if (installedOn is { } i && warrantyExpiresOn is { } w && w < i)
            throw new ArgumentException("Warranty expiry cannot precede installation.", nameof(warrantyExpiresOn));
        InstalledOn = installedOn;
        WarrantyExpiresOn = warrantyExpiresOn;
        ExpectedServiceLifeYears = expectedServiceLifeYears;
    }

    public void RecordCondition(AssetCondition condition) => Condition = condition;

    public void SetReplacementCost(decimal? estimate)
    {
        if (estimate is < 0m)
            throw new ArgumentOutOfRangeException(nameof(estimate), "Replacement cost cannot be negative.");
        ReplacementCostEstimate = estimate is null ? null : decimal.Round(estimate.Value, 2);
    }

    public void SetNotes(string? notes) => Notes = MessageText.OptionalBody(notes, nameof(notes), 4000);

    public bool IsUnderWarranty(DateOnly asOf) => WarrantyExpiresOn is { } expiry && asOf <= expiry;

    public int? AgeInYears(DateOnly asOf)
    {
        if (InstalledOn is not { } installed || asOf < installed) return null;
        var years = asOf.Year - installed.Year;
        if (asOf < installed.AddYears(years)) years--;
        return years;
    }

    private static string? Optional(string? value, string parameter, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : MessageText.RequireSingleLine(value, parameter, maxLength);
}
