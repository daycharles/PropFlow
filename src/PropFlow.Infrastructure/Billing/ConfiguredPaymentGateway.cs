using Microsoft.Extensions.Configuration;
using PropFlow.Application.Billing;

namespace PropFlow.Infrastructure.Billing;

// FS-S08 ships no real processor integration. This stands in for one and, crucially, makes
// the outage and decline paths reachable from a test and from a local demo:
//   Billing:GatewayMode = Available (default) | Unavailable | Declined
// A real adapter replaces this class without touching the endpoints, which only see
// IPaymentGateway.
public sealed class ConfiguredPaymentGateway(IConfiguration configuration) : IPaymentGateway
{
    public Task<PaymentGatewayResult> AuthorizeAsync(PaymentGatewayRequest request, CancellationToken ct)
    {
        var mode = configuration["Billing:GatewayMode"];
        if (string.Equals(mode, "Unavailable", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new PaymentGatewayResult(PaymentGatewayOutcome.Unavailable, null, "The payment provider is unavailable."));
        if (string.Equals(mode, "Declined", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new PaymentGatewayResult(PaymentGatewayOutcome.Declined, null, "The payment was declined."));
        return Task.FromResult(new PaymentGatewayResult(PaymentGatewayOutcome.Accepted, request.Reference, null));
    }
}
