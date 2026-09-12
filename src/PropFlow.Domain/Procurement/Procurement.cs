using PropFlow.Domain;

namespace PropFlow.Domain.Procurement;

public enum VendorDocumentType { Insurance, License, Certification, Other }
public enum ProcurementStatus { Draft, PendingApproval, Approved, Rejected, Closed }
public enum PurchaseOrderStatus { Draft, PendingApproval, Approved, Issued, PartiallyInvoiced, Invoiced, Closed, Rejected }

public sealed class VendorProfile(Guid organizationId, Guid id, Guid vendorId, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid VendorId { get; private set; } = vendorId == Guid.Empty ? throw new ArgumentException("Vendor is required.", nameof(vendorId)) : vendorId;
    public string? TaxIdentifier { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = createdAt.ToUniversalTime();
    public void Update(string? taxIdentifier, string? address, string? notes)
    { TaxIdentifier = Trim(taxIdentifier, 100); Address = Trim(address, 500); Notes = Trim(notes, 2000); }
    private static string? Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var x && x.Length <= max ? x : throw new ArgumentException($"Value must be at most {max} characters.");
}

public sealed class VendorDocument(Guid organizationId, Guid id, Guid vendorId, VendorDocumentType type, string documentNumber, DateOnly expiresOn, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid VendorId { get; private set; } = vendorId == Guid.Empty ? throw new ArgumentException("Vendor is required.", nameof(vendorId)) : vendorId;
    public VendorDocumentType Type { get; private set; } = type;
    public string DocumentNumber { get; private set; } = Required(documentNumber, 100);
    public DateOnly ExpiresOn { get; private set; } = expiresOn;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt.ToUniversalTime();
    public bool IsExpired(DateOnly on) => ExpiresOn < on;
    public void Deactivate() => IsActive = false;
    private static string Required(string value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw new ArgumentException($"Document number must contain 1 to {max} characters.") : value.Trim();
}

public sealed class VendorContract(Guid organizationId, Guid id, Guid vendorId, string name, DateOnly startsOn, DateOnly? endsOn, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid VendorId { get; private set; } = vendorId == Guid.Empty ? throw new ArgumentException("Vendor is required.", nameof(vendorId)) : vendorId;
    public string Name { get; private set; } = Required(name, 200);
    public DateOnly StartsOn { get; private set; } = startsOn;
    public DateOnly? EndsOn { get; private set; } = endsOn;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt.ToUniversalTime();
    public void Terminate() => IsActive = false;
    private static string Required(string value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw new ArgumentException($"Name must contain 1 to {max} characters.") : value.Trim();
}

public sealed class VendorRateCard(Guid organizationId, Guid id, Guid vendorId, string serviceCode, decimal unitRate, DateOnly effectiveOn, DateOnly? expiresOn) : TenantEntity(organizationId, id)
{
    public Guid VendorId { get; private set; } = vendorId == Guid.Empty ? throw new ArgumentException("Vendor is required.", nameof(vendorId)) : vendorId;
    public string ServiceCode { get; private set; } = string.IsNullOrWhiteSpace(serviceCode) || serviceCode.Trim().Length > 100 ? throw new ArgumentException("Service code is required.") : serviceCode.Trim();
    public decimal UnitRate { get; private set; } = unitRate >= 0 ? unitRate : throw new ArgumentOutOfRangeException(nameof(unitRate));
    public DateOnly EffectiveOn { get; private set; } = effectiveOn;
    public DateOnly? ExpiresOn { get; private set; } = expiresOn;
}

public sealed class ProcurementBid(Guid organizationId, Guid id, Guid vendorId, Guid? workItemId, string title, decimal amount, DateTimeOffset submittedAt) : TenantEntity(organizationId, id)
{
    public Guid VendorId { get; private set; } = vendorId == Guid.Empty ? throw new ArgumentException("Vendor is required.", nameof(vendorId)) : vendorId;
    public Guid? WorkItemId { get; private set; } = workItemId;
    public string Title { get; private set; } = Required(title, 200);
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public DateTimeOffset SubmittedAt { get; private set; } = submittedAt.ToUniversalTime();
    public bool IsSelected { get; private set; }
    public void Select() => IsSelected = true;
    private static string Required(string value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw new ArgumentException($"Title must contain 1 to {max} characters.") : value.Trim();
}

public sealed class PurchaseOrder(Guid organizationId, Guid id, Guid vendorId, Guid? propertyId, string number, decimal amount, decimal approvalThreshold, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid VendorId { get; private set; } = vendorId == Guid.Empty ? throw new ArgumentException("Vendor is required.", nameof(vendorId)) : vendorId;
    public Guid? PropertyId { get; private set; } = propertyId;
    public string Number { get; private set; } = Required(number, 50);
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public decimal ApprovalThreshold { get; private set; } = approvalThreshold > 0 ? approvalThreshold : throw new ArgumentOutOfRangeException(nameof(approvalThreshold));
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt.ToUniversalTime();
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public void Submit() { if (Status != PurchaseOrderStatus.Draft) throw new InvalidOperationException("Only a draft purchase order can be submitted."); Status = Amount > ApprovalThreshold ? PurchaseOrderStatus.PendingApproval : PurchaseOrderStatus.Approved; }
    public void Approve(Guid actor, DateTimeOffset at) { if (Status != PurchaseOrderStatus.PendingApproval) throw new InvalidOperationException("Only a purchase order pending approval can be approved."); Status = PurchaseOrderStatus.Approved; ApprovedBy = actor; ApprovedAt = at.ToUniversalTime(); }
    public void Issue() { if (Status != PurchaseOrderStatus.Approved) throw new InvalidOperationException("Only an approved purchase order can be issued."); Status = PurchaseOrderStatus.Issued; }
    public void Match(decimal invoiceAmount) { if (Status is not (PurchaseOrderStatus.Issued or PurchaseOrderStatus.PartiallyInvoiced)) throw new InvalidOperationException("Only an issued purchase order can be invoiced."); if (invoiceAmount <= 0 || invoiceAmount > Amount) throw new InvalidOperationException("Invoice exceeds the purchase order amount."); Status = invoiceAmount == Amount ? PurchaseOrderStatus.Invoiced : PurchaseOrderStatus.PartiallyInvoiced; }
    private static string Required(string value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw new ArgumentException($"Number must contain 1 to {max} characters.") : value.Trim();
}

public sealed class WorkAuthorization(Guid organizationId, Guid id, Guid vendorId, Guid workItemId, decimal amount, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid VendorId { get; private set; } = vendorId;
    public Guid WorkItemId { get; private set; } = workItemId;
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public ProcurementStatus Status { get; private set; } = ProcurementStatus.Draft;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt.ToUniversalTime();
    public void Approve() { if (Status != ProcurementStatus.Draft) throw new InvalidOperationException("Only a draft authorization can be approved."); Status = ProcurementStatus.Approved; }
}

public sealed class VendorPerformanceReview(Guid organizationId, Guid id, Guid vendorId, int score, string? notes, DateTimeOffset reviewedAt) : TenantEntity(organizationId, id)
{
    public Guid VendorId { get; private set; } = vendorId;
    public int Score { get; private set; } = score is >= 1 and <= 5 ? score : throw new ArgumentOutOfRangeException(nameof(score));
    public string? Notes { get; private set; } = notes;
    public DateTimeOffset ReviewedAt { get; private set; } = reviewedAt.ToUniversalTime();
}

public sealed class PurchaseOrderInvoiceMatch(Guid organizationId, Guid id, Guid purchaseOrderId, Guid payableInvoiceId, decimal amount, DateTimeOffset matchedAt) : TenantEntity(organizationId, id)
{
    public Guid PurchaseOrderId { get; private set; } = purchaseOrderId;
    public Guid PayableInvoiceId { get; private set; } = payableInvoiceId;
    public decimal Amount { get; private set; } = amount;
    public DateTimeOffset MatchedAt { get; private set; } = matchedAt.ToUniversalTime();
}
