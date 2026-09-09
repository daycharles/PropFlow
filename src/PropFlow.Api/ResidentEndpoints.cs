using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Communications;
using PropFlow.Domain.People;
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

        group.MapPost("/", async (ResidentRequest request, OperationsStore store, ITenantContext tenant, CancellationToken ct) =>
        {
            Resident resident;
            try
            {
                resident = new Resident(tenant.OrganizationId, Guid.NewGuid(), request.FullName, request.Email, request.Phone);
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            store.Residents.Add(resident);
            await store.SaveChangesAsync(ct);
            return Results.Created($"/api/residents/{resident.Id}", resident);
        }).RequireAuthorization(Capabilities.ManagePeople);

        group.MapPut("/{id:guid}", async (Guid id, ResidentRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var resident = await store.Residents.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (resident is null) return Results.NotFound();

            try
            {
                resident.Rename(request.FullName);
                resident.UpdateContact(request.Email, request.Phone);
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            await store.SaveChangesAsync(ct);
            return Results.Ok(resident);
        }).RequireAuthorization(Capabilities.ManagePeople);

        group.MapPut("/{id:guid}/consent", async (Guid id, ConsentRequest request, OperationsStore store,
            TimeProvider clock, CancellationToken ct) =>
        {
            var resident = await store.Residents.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (resident is null) return Results.NotFound();

            try
            {
                resident.SetConsent(request.Channel, request.Granted, clock.GetUtcNow());
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            await store.SaveChangesAsync(ct);
            return Results.Ok(resident);
        }).RequireAuthorization(Capabilities.ManagePeople);

        group.MapPost("/{id:guid}/occupancies", async (Guid id, OccupancyRequest request, OperationsStore store, ITenantContext tenant, CancellationToken ct) =>
        {
            if (!await store.Residents.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();

            Occupancy occupancy;
            try
            {
                occupancy = new Occupancy(tenant.OrganizationId, Guid.NewGuid(), id, request.SpaceId, request.MovedInOn);
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            store.Occupancies.Add(occupancy);
            try
            {
                await store.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // The space belongs to another organization or does not exist.
                return Results.Problem(statusCode: 400, title: "Unknown space");
            }
            return Results.Created($"/api/residents/{id}/occupancies/{occupancy.Id}", occupancy);
        }).RequireAuthorization(Capabilities.ManagePeople);

        group.MapPut("/{id:guid}/occupancies/{occupancyId:guid}/end", async (Guid id, Guid occupancyId,
            EndOccupancyRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var occupancy = await store.Occupancies.SingleOrDefaultAsync(x => x.Id == occupancyId && x.ResidentId == id, ct);
            if (occupancy is null) return Results.NotFound();

            try
            {
                occupancy.EndOn(request.MovedOutOn);
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            await store.SaveChangesAsync(ct);
            return Results.Ok(occupancy);
        }).RequireAuthorization(Capabilities.ManagePeople);
    }
}

// Tenant comes from the verified session.
public sealed record ResidentRequest(string FullName, string? Email, string? Phone);
public sealed record ConsentRequest(MessageChannel Channel, bool Granted);
public sealed record OccupancyRequest(Guid SpaceId, DateOnly MovedInOn);
public sealed record EndOccupancyRequest(DateOnly MovedOutOn);
