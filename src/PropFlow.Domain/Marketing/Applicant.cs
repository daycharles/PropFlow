namespace PropFlow.Domain.Marketing;

public enum ApplicantStatus { New, Screening, Approved, Declined }

public sealed class Applicant(Guid organizationId, Guid id, Guid listingId, Guid inquiryId, string prospectName, string email) : TenantEntity(organizationId, id)
{
    public Guid ListingId { get; private set; } = listingId == Guid.Empty ? throw new ArgumentException("Listing is required.", nameof(listingId)) : listingId;
    public Guid InquiryId { get; private set; } = inquiryId == Guid.Empty ? throw new ArgumentException("Inquiry is required.", nameof(inquiryId)) : inquiryId;
    public string ProspectName { get; private set; } = Required(prospectName, nameof(prospectName), 200);
    public string Email { get; private set; } = Required(email, nameof(email), 254);
    public ApplicantStatus Status { get; private set; } = ApplicantStatus.New;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public void SetStatus(ApplicantStatus status) => Status = status;
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
