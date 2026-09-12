using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Configuration;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// PF-S03.06. Business hours + org default time zone. Storage and validation only - no consumer
// reads this yet (no "closed for business" check anywhere), matching NumberingEndpoints' and
// ApprovalEndpoints' shape before it: an upserting PUT, and a GET that never 404s.
public static class OrganizationSettingsEndpoints
{
    public static void MapOrganizationSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings/organization").RequireAuthorization(Capabilities.ReadWork);

        // No row yet just means "never configured" - report all-closed/no-default-zone rather
        // than 404, so a caller can render a settings page before anyone has saved one.
        group.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
        {
            var settings = await store.OrganizationSettings.AsNoTracking().SingleOrDefaultAsync(ct);
            return Results.Ok(settings is null ? DefaultResponse() : ToResponse(settings));
        });

        group.MapPut("/", async (OrganizationSettingsRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            try
            {
                var hours = request.BusinessHours.Select(h => new BusinessHoursWindow(h.Day, h.Open, h.Close)).ToArray();
                var settings = await store.OrganizationSettings.SingleOrDefaultAsync(ct);
                if (settings is null)
                {
                    settings = OrganizationSettings.Create(store.OrganizationId, Guid.NewGuid(), request.DefaultTimeZoneId, clock.GetUtcNow());
                    store.OrganizationSettings.Add(settings);
                }
                else
                {
                    settings.SetDefaultTimeZone(request.DefaultTimeZoneId);
                }
                settings.SetBusinessHours(hours);
                await store.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(settings));
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.ManageConfiguration);
    }

    private static OrganizationSettingsResponse DefaultResponse() => new(null,
        Enum.GetValues<DayOfWeek>().Select(d => new BusinessHoursWindowResponse(d, null, null)).ToArray());

    private static OrganizationSettingsResponse ToResponse(OrganizationSettings x) => new(x.DefaultTimeZoneId,
        x.ReadBusinessHours().Select(h => new BusinessHoursWindowResponse(h.Day, h.Open, h.Close)).ToArray());
}

public sealed record BusinessHoursWindowRequest(DayOfWeek Day, TimeOnly? Open, TimeOnly? Close);
public sealed record OrganizationSettingsRequest(string DefaultTimeZoneId, IReadOnlyList<BusinessHoursWindowRequest> BusinessHours);
public sealed record BusinessHoursWindowResponse(DayOfWeek Day, TimeOnly? Open, TimeOnly? Close);
public sealed record OrganizationSettingsResponse(string? DefaultTimeZoneId, IReadOnlyList<BusinessHoursWindowResponse> BusinessHours);
