using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;

namespace PropFlow.Infrastructure.Communications;

public sealed class TwilioMessageSender(IHttpClientFactory clients, CommunicationsOptions options) : IMessageSender
{
    public MessageChannel Channel => MessageChannel.Sms;

    public async Task<MessageDeliveryResult> SendAsync(OutboundMessage message, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(options.TwilioAccountSid!)}/Messages.json");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.TwilioAccountSid}:{options.TwilioAuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = message.RecipientAddress, ["From"] = options.TwilioFromNumber!, ["Body"] = message.Body
        });
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await SendAsync(clients.CreateClient(), request, cancellationToken);
    }

    private static async Task<MessageDeliveryResult> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return MessageDeliveryResult.Rejected($"Provider returned {(int)response.StatusCode}.");
        try
        {
            using var json = JsonDocument.Parse(body);
            return MessageDeliveryResult.Delivered(json.RootElement.GetProperty("sid").GetString() ?? "twilio-accepted");
        }
        catch (JsonException) { return MessageDeliveryResult.Delivered("twilio-accepted"); }
    }
}

public sealed class SendGridMessageSender(IHttpClientFactory clients, CommunicationsOptions options) : IMessageSender
{
    public MessageChannel Channel => MessageChannel.Email;

    public async Task<MessageDeliveryResult> SendAsync(OutboundMessage message, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SendGridApiKey);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new
        {
            personalizations = new[] { new { to = new[] { new { email = message.RecipientAddress } } } },
            from = new { email = options.SendGridFromAddress },
            subject = message.Subject,
            content = new[] { new { type = "text/plain", value = message.Body } }
        });
        using var response = await clients.CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode
            ? MessageDeliveryResult.Delivered(response.Headers.TryGetValues("X-Message-Id", out var values) ? values.SingleOrDefault() ?? "sendgrid-accepted" : "sendgrid-accepted")
            : MessageDeliveryResult.Rejected($"Provider returned {(int)response.StatusCode}.");
    }
}
