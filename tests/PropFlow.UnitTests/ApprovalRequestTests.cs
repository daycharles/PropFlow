using PropFlow.Domain.Approvals;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ApprovalRequestTests
{
    private static readonly Guid Requester = Guid.NewGuid();

    private static ApprovalRequest Create(string? note = null) =>
        ApprovalRequest.Create(Guid.NewGuid(), Guid.NewGuid(), "Budget", Guid.NewGuid(), Requester, note, DateTimeOffset.UtcNow);

    [Fact]
    public void A_new_request_is_pending()
    {
        var request = Create();
        Assert.Equal(ApprovalStatus.Pending, request.Status);
        Assert.Null(request.DecidedBy);
        Assert.Null(request.DecidedAt);
    }

    [Fact]
    public void An_empty_subject_type_or_subject_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ApprovalRequest.Create(Guid.NewGuid(), Guid.NewGuid(), "", Guid.NewGuid(), Requester, null, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => ApprovalRequest.Create(Guid.NewGuid(), Guid.NewGuid(), "Budget", Guid.Empty, Requester, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void An_empty_requester_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ApprovalRequest.Create(Guid.NewGuid(), Guid.NewGuid(), "Budget", Guid.NewGuid(), Guid.Empty, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Approve_records_the_decider_and_reason()
    {
        var request = Create();
        var decider = Guid.NewGuid();
        request.Approve(decider, DateTimeOffset.UtcNow, "Looks right");
        Assert.Equal(ApprovalStatus.Approved, request.Status);
        Assert.Equal(decider, request.DecidedBy);
        Assert.Equal("Looks right", request.DecisionReason);
    }

    [Fact]
    public void Reject_records_the_decider_and_reason()
    {
        var request = Create();
        var decider = Guid.NewGuid();
        request.Reject(decider, DateTimeOffset.UtcNow, "Not this quarter");
        Assert.Equal(ApprovalStatus.Rejected, request.Status);
        Assert.Equal("Not this quarter", request.DecisionReason);
    }

    [Fact]
    public void A_decision_reason_is_optional()
    {
        var request = Create();
        request.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Null(request.DecisionReason);
    }

    [Fact]
    public void The_requester_cannot_decide_their_own_request()
    {
        var request = Create();
        Assert.Throws<InvalidOperationException>(() => request.Approve(Requester, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => request.Reject(Requester, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_decided_request_cannot_be_decided_again()
    {
        var request = Create();
        request.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => request.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => request.Reject(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_note_or_reason_over_one_thousand_characters_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Create(note: new string('x', 1001)));
        var request = Create();
        Assert.Throws<ArgumentException>(() => request.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow, new string('x', 1001)));
    }
}
