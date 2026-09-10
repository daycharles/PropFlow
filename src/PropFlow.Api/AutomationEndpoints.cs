using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Automation;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class AutomationEndpoints
{
    public static void MapAutomationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/automation/rules").RequireAuthorization(Capabilities.ManageAutomationRules);
        group.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok((await store.AutomationRules.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct))
                .Select(ToResponse).ToList()));
        group.MapPost("/", async (CreateAutomationRuleRequest request, OperationsStore store, ITenantContext tenant, CancellationToken ct) =>
        {
            try
            {
                var rule = new AutomationRule(tenant.OrganizationId, Guid.NewGuid(), request.Name, request.Trigger,
                    request.Conditions ?? [], request.Actions, DateTimeOffset.UtcNow);
                store.AutomationRules.Add(rule);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/automation/rules/{rule.Id}", ToResponse(rule));
            }
            catch (ArgumentException ex) { return Results.Problem(statusCode: 400, title: ex.Message); }
        });
        group.MapPost("/{id:guid}/enabled", async (Guid id, SetAutomationRuleEnabledRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var rule = await store.AutomationRules.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (rule is null) return Results.NotFound();
            if (request.Enabled) rule.Enable(); else rule.Disable();
            await store.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(rule));
        });
    }
    private static AutomationRuleResponse ToResponse(AutomationRule x) =>
        new(x.Id, x.Name, x.Trigger, x.ReadConditions(), x.ReadActions(), x.IsEnabled, x.CreatedAt, x.UpdatedAt);
}
public sealed record CreateAutomationRuleRequest(string Name, AutomationTrigger Trigger,
    IReadOnlyList<AutomationCondition>? Conditions, IReadOnlyList<AutomationAction> Actions);
public sealed record SetAutomationRuleEnabledRequest(bool Enabled);
public sealed record AutomationRuleResponse(Guid Id, string Name, AutomationTrigger Trigger,
    IReadOnlyList<AutomationCondition> Conditions, IReadOnlyList<AutomationAction> Actions,
    bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
