using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class ResidentEndpoints
{
    public static void MapResidentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/residents").RequireAuthorization(Capabilities.ReadWork);

        group.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Residents.AsNoTracking()
                .OrderBy(x => x.FullName).ThenBy(x => x.Id).Take(200).ToListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
            await store.Residents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } resident
                ? Results.Ok(resident) : Results.NotFound());

        group.MapGet("/{id:guid}/occupancies", async (Guid id, OperationsStore store, CancellationToken ct) =>
            await store.Residents.AnyAsync(x => x.Id == id, ct)
                ? Results.Ok(await store.Occupancies.AsNoTracking().Where(x => x.ResidentId == id)
                    .OrderByDescending(x => x.MovedInOn).ThenBy(x => x.Id).ToListAsync(ct))
                : Results.NotFound());
    }
}
