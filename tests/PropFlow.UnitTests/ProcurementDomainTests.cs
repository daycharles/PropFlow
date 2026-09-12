using PropFlow.Domain.Procurement;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ProcurementDomainTests
{
    private readonly Guid org = Guid.NewGuid();
    [Fact]
    public void Purchase_order_above_threshold_requires_approval_and_cannot_issue_early()
    {
        var po = new PurchaseOrder(org, Guid.NewGuid(), Guid.NewGuid(), null, "PO-1", 1200, 1000, DateTimeOffset.UtcNow);
        po.Submit();
        Assert.Equal(PurchaseOrderStatus.PendingApproval, po.Status);
        Assert.Throws<InvalidOperationException>(() => po.Issue());
        po.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow);
        po.Issue();
        Assert.Equal(PurchaseOrderStatus.Issued, po.Status);
    }

    [Fact]
    public void Invoice_match_cannot_exceed_purchase_order()
    {
        var po = new PurchaseOrder(org, Guid.NewGuid(), Guid.NewGuid(), null, "PO-2", 100, 1000, DateTimeOffset.UtcNow);
        po.Submit(); po.Issue();
        Assert.Throws<InvalidOperationException>(() => po.Match(101));
        po.Match(100);
        Assert.Equal(PurchaseOrderStatus.Invoiced, po.Status);
    }

    [Fact]
    public void Vendor_document_expiry_is_date_based()
    {
        var d = new VendorDocument(org, Guid.NewGuid(), Guid.NewGuid(), VendorDocumentType.Insurance, "CERT-1", new DateOnly(2026, 9, 12), DateTimeOffset.UtcNow);
        Assert.False(d.IsExpired(new DateOnly(2026, 9, 12)));
        Assert.True(d.IsExpired(new DateOnly(2026, 9, 13)));
    }

    [Fact]
    public void Work_authorization_has_explicit_approval_transition()
    {
        var a = new WorkAuthorization(org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 50, DateTimeOffset.UtcNow);
        Assert.Equal(ProcurementStatus.Draft, a.Status);
        a.Approve();
        Assert.Equal(ProcurementStatus.Approved, a.Status);
    }
}
