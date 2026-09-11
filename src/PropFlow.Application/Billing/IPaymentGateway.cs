namespace PropFlow.Application.Billing;

// Outcomes, not exceptions (.claude/rules/architecture.md). Unavailable is the provider
// outage case and is deliberately distinct from Declined: a decline is a final answer about
// the money, an outage is no answer at all and must leave no partial state behind.
public enum PaymentGatewayOutcome { Accepted, Declined, Unavailable }

public sealed record PaymentGatewayRequest(Guid OrganizationId, Guid LeaseId, Guid ResidentId, decimal Amount, string Reference);

public sealed record PaymentGatewayResult(PaymentGatewayOutcome Outcome, string? ProviderReference, string? FailureReason);

public interface IPaymentGateway
{
    Task<PaymentGatewayResult> AuthorizeAsync(PaymentGatewayRequest request, CancellationToken ct);
}
