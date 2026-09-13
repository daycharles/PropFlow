using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class ProviderCallbackEndpoints
{
    private const int MaxBodyBytes = 32 * 1024;

    public static void MapProviderCallbackEndpoints(this WebApplication app)
    {
        app.MapPost("/api/communications/provider-callback", async (HttpRequest request, IConfiguration configuration, CancellationToken ct) =>
        {
            if (request.ContentLength is > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var signature = request.Headers["X-PropFlow-Signature"].ToString();
            var secret = configuration["Communications:ProviderCallbackSecret"];
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature)) return Results.Unauthorized();
            using var body = new MemoryStream();
            await request.Body.CopyToAsync(body, ct);
            if (body.Length > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body.ToArray()));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature))) return Results.Unauthorized();

            ProviderCallbackPayload? payload;
            try { payload = JsonSerializer.Deserialize<ProviderCallbackPayload>(body.ToArray()); }
            catch (JsonException) { return Results.BadRequest(); }
            if (payload is null || payload.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(payload.ProviderReference) ||
                !Enum.TryParse<ProviderDeliveryStatus>(payload.Status, true, out var status) || status == ProviderDeliveryStatus.Unknown)
                return Results.BadRequest();

            var connection = configuration.GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(connection)) return Results.Problem(statusCode: 503, title: "Database is not configured");
            await using var store = DatabaseProvisioner.CreateCommunicationsStore(connection, payload.OrganizationId);
            var messages = await store.OutboxMessages.Where(x => x.ProviderReference == payload.ProviderReference).ToListAsync(ct);
            if (messages.Count == 0) return Results.NotFound();
            var digest = Convert.ToHexString(SHA256.HashData(body.ToArray()));
            if (await store.ProviderCallbackReceipts.AnyAsync(x => x.BodyDigest == digest, ct)) return Results.NoContent();
            store.ProviderCallbackReceipts.Add(new ProviderCallbackReceipt(payload.OrganizationId, Guid.NewGuid(), digest, DateTimeOffset.UtcNow));
            try
            {
                foreach (var message in messages) message.ApplyProviderCallback(status, payload.FailureReason, payload.OccurredAt ?? DateTimeOffset.UtcNow);
                await store.SaveChangesAsync(ct);
            }
            catch (ArgumentException exception) { return Results.BadRequest(exception.Message); }
            return Results.NoContent();
        });
    }
}

public sealed record ProviderCallbackPayload(Guid OrganizationId, string ProviderReference, string Status,
    string? FailureReason, DateTimeOffset? OccurredAt);
