namespace PropFlow.Domain.Marketing;

public enum ListingStatus { Draft, Published, Archived }
public enum InquiryStatus { New, Contacted, Closed }
public enum ShowingStatus { Requested, Confirmed, Completed, Cancelled }

public sealed class Listing(Guid organizationId, Guid id, Guid propertyId, Guid? spaceId, string headline, string? description, DateOnly? availableOn, decimal? monthlyRent)
    : TenantEntity(organizationId, id)
{
    public Guid PropertyId { get; private set; } = RequiredId(propertyId, nameof(propertyId));
    public Guid? SpaceId { get; private set; } = spaceId;
    public string Headline { get; private set; } = Required(headline, nameof(headline), 200);
    public string? Description { get; private set; } = Optional(description, 4000);
    public DateOnly? AvailableOn { get; private set; } = availableOn;
    public decimal? MonthlyRent { get; private set; } = monthlyRent is >= 0 ? monthlyRent : throw new ArgumentOutOfRangeException(nameof(monthlyRent));
    public ListingStatus Status { get; private set; } = ListingStatus.Draft;

    public void Update(Guid propertyId, Guid? spaceId, string headline, string? description, DateOnly? availableOn, decimal? monthlyRent)
    {
        PropertyId = RequiredId(propertyId, nameof(propertyId)); SpaceId = spaceId;
        Headline = Required(headline, nameof(headline), 200); Description = Optional(description, 4000);
        AvailableOn = availableOn; MonthlyRent = monthlyRent is >= 0 ? monthlyRent : throw new ArgumentOutOfRangeException(nameof(monthlyRent));
    }
    public void Publish() => Status = ListingStatus.Published;
    public void Unpublish() { if (Status == ListingStatus.Published) Status = ListingStatus.Draft; }
    public void Archive() => Status = ListingStatus.Archived;

    private static Guid RequiredId(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException("ID is required.", name) : value;
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Required(value, nameof(value), max);
}

public sealed class Inquiry(Guid organizationId, Guid id, Guid listingId, string prospectName, string email, string? phone, string? message, string? leadSource)
    : TenantEntity(organizationId, id)
{
    public Guid ListingId { get; private set; } = listingId != Guid.Empty ? listingId : throw new ArgumentException("Listing is required.", nameof(listingId));
    public string ProspectName { get; private set; } = ListingValidation.Require(prospectName, nameof(prospectName), 200);
    public string Email { get; private set; } = ListingValidation.Require(email, nameof(email), 254);
    public string? Phone { get; private set; } = string.IsNullOrWhiteSpace(phone) ? null : ListingValidation.Require(phone, nameof(phone), 40);
    public string? Message { get; private set; } = string.IsNullOrWhiteSpace(message) ? null : ListingValidation.Require(message, nameof(message), 2000);
    public string? LeadSource { get; private set; } = string.IsNullOrWhiteSpace(leadSource) ? null : ListingValidation.Require(leadSource, nameof(leadSource), 100);
    public InquiryStatus Status { get; private set; } = InquiryStatus.New;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public void SetStatus(InquiryStatus status) => Status = status;
}

public sealed class Showing(Guid organizationId, Guid id, Guid listingId, string prospectName, DateTimeOffset scheduledAt)
    : TenantEntity(organizationId, id)
{
    public Guid ListingId { get; private set; } = listingId != Guid.Empty ? listingId : throw new ArgumentException("Listing is required.", nameof(listingId));
    public string ProspectName { get; private set; } = ListingValidation.Require(prospectName, nameof(prospectName), 200);
    public DateTimeOffset ScheduledAt { get; private set; } = scheduledAt.ToUniversalTime();
    public ShowingStatus Status { get; private set; } = ShowingStatus.Requested;
    public void SetStatus(ShowingStatus status) => Status = status;
}

internal static class ListingValidation
{
    internal static string Require(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
