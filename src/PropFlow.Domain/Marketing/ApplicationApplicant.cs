namespace PropFlow.Domain.Marketing;

public enum ApplicantRole { Primary = 0, CoApplicant = 1, Guarantor = 2 }

// The join between a RentalApplication and an Applicant (the person), carrying the per-
// application PII this story stores. It is a separate row rather than fields on Applicant
// because Applicant is unique per inquiry (OperationsStore.cs) — putting application state on
// the person would permanently block joint applications and re-applications.
//
// What is stored is the whole list: monthly income and employment status. SSN, date of birth,
// driver's licence, the raw credit report and bank details are deliberately absent — the
// applicant gives those to the screening provider through a provider-hosted flow and PropFlow
// keeps only the idempotency key and the verdict, the same posture as IPaymentGateway and
// IntegrationConnection. Adding a column here forces encryption-at-rest, a retention sweep and
// a deletion API that FS-S05 does not fund.
public sealed class ApplicationApplicant : TenantEntity
{
    public const int EmploymentStatusMaxLength = 100;

    // EF materialization.
    private ApplicationApplicant(Guid organizationId, Guid id) : base(organizationId, id) { }

    public ApplicationApplicant(
        Guid organizationId,
        Guid id,
        Guid applicationId,
        Guid applicantId,
        ApplicantRole role,
        decimal? monthlyIncome = null,
        string? employmentStatus = null)
        : base(organizationId, id)
    {
        ApplicationId = ApplicationText.RequireId(applicationId, nameof(applicationId));
        ApplicantId = ApplicationText.RequireId(applicantId, nameof(applicantId));
        Role = role;
        MonthlyIncome = RequireNonNegative(monthlyIncome);
        EmploymentStatus = ApplicationText.OptionalSingleLine(employmentStatus, nameof(employmentStatus), EmploymentStatusMaxLength);
    }

    public Guid ApplicationId { get; private set; }
    public Guid ApplicantId { get; private set; }
    public ApplicantRole Role { get; private set; }
    public decimal? MonthlyIncome { get; private set; }
    public string? EmploymentStatus { get; private set; }

    public void SetFinancials(decimal? monthlyIncome, string? employmentStatus)
    {
        MonthlyIncome = RequireNonNegative(monthlyIncome);
        EmploymentStatus = ApplicationText.OptionalSingleLine(employmentStatus, nameof(employmentStatus), EmploymentStatusMaxLength);
    }

    private static decimal? RequireNonNegative(decimal? monthlyIncome) =>
        monthlyIncome is < 0 ? throw new ArgumentOutOfRangeException(nameof(monthlyIncome)) : monthlyIncome;
}
