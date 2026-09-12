using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Configuration;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// PF-S03.04. Configuring a scheme's prefix/width is here; allocating an actual number happens
// atomically in EfWorkOperations at work creation, not through this file.
public static class NumberingEndpoints
{
    public static void MapNumberingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings/numbering").RequireAuthorization(Capabilities.ReadWork);

        group.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok((await store.NumberingSequences.AsNoTracking().OrderBy(x => x.AppliesTo).ToListAsync(ct))
                .Select(ToResponse).ToList()));

        // Upsert by design: PUT either creates a scheme (starting at 1) or reconfigures an
        // existing one's prefix/width. There is no way to change NextValue through this endpoint
        // - rewinding it below an already-issued number is exactly the collision the atomic
        // allocator exists to prevent, so that footgun is not offered.
        group.MapPut("/{appliesTo}", async (ConfigurationEntityType appliesTo, NumberingRequest request,
            OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var sequence = await store.NumberingSequences.SingleOrDefaultAsync(x => x.AppliesTo == appliesTo, ct);
            try
            {
                if (sequence is null)
                {
                    sequence = NumberingSequence.Create(store.OrganizationId, Guid.NewGuid(), appliesTo, request.Prefix, request.Width, clock.GetUtcNow());
                    store.NumberingSequences.Add(sequence);
                }
                else
                {
                    sequence.Configure(request.Prefix, request.Width);
                }
                await store.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(sequence));
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.ManageConfiguration);
    }

    private static NumberingResponse ToResponse(NumberingSequence x) =>
        new(x.AppliesTo, x.Prefix, x.Width, x.NextValue, x.FormatNext());
}

public sealed record NumberingRequest(string? Prefix, int Width);
public sealed record NumberingResponse(ConfigurationEntityType AppliesTo, string Prefix, int Width, long NextValue, string NextFormatted);
