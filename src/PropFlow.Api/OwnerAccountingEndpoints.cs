using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Accounting;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class OwnerAccountingEndpoints
{
    public static void MapOwnerAccountingEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/owner-accounting").RequireAuthorization(Capabilities.ManageAccounting);
        admin.MapGet("/budgets", async (Guid? propertyId, int? year, OperationsStore s, CancellationToken ct) => Results.Ok(await s.Budgets.AsNoTracking().Where(x => propertyId == null || x.PropertyId == propertyId).Where(x => year == null || x.Year == year).OrderByDescending(x => x.Year).ToListAsync(ct)));
        admin.MapPost("/budgets", async (BudgetRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await s.Properties.AnyAsync(x => x.Id == r.PropertyId, ct)) return Results.NotFound("Property was not found.");
            if (r.Lines is not null && await s.ChartOfAccounts.CountAsync(x => r.Lines.Select(l => l.AccountId).Contains(x.Id), ct) != r.Lines.Select(l => l.AccountId).Distinct().Count()) return Results.BadRequest("Every budget account must exist in this organization.");
            if (await s.Budgets.AnyAsync(x => x.PropertyId == r.PropertyId && x.Year == r.Year, ct)) return Results.Conflict("A budget already exists for this property and year.");
            try { var b = new Budget(s.OrganizationId, Guid.NewGuid(), r.PropertyId, r.Year, r.Name, clock.GetUtcNow()); s.Budgets.Add(b); if (r.Lines is not null) { var lines = r.Lines.Select(x => new BudgetLine(s.OrganizationId, Guid.NewGuid(), b.Id, x.AccountId, x.Month, x.Amount)).ToArray(); s.BudgetLines.AddRange(lines); } await s.SaveChangesAsync(ct); return Results.Created($"/api/owner-accounting/budgets/{b.Id}", b); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        admin.MapGet("/budgets/{id:guid}", async (Guid id, OperationsStore s, CancellationToken ct) => { var b = await s.Budgets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); return b is null ? Results.NotFound() : Results.Ok(new { budget = b, lines = await s.BudgetLines.AsNoTracking().Where(x => x.BudgetId == id).OrderBy(x => x.Month).ThenBy(x => x.AccountId).ToListAsync(ct) }); });
        admin.MapPost("/budgets/{id:guid}/submit", async (Guid id, OperationsStore s, CancellationToken ct) => await ChangeBudget(id, s, ct, x => x.Submit()));
        admin.MapPost("/budgets/{id:guid}/approve", async (Guid id, ClaimsPrincipal user, OperationsStore s, TimeProvider clock, CancellationToken ct) => await ChangeBudget(id, s, ct, x => x.Approve(WorkScopeAccess.Actor(user), clock.GetUtcNow())));
        admin.MapPost("/budgets/{id:guid}/reject", async (Guid id, OperationsStore s, CancellationToken ct) => await ChangeBudget(id, s, ct, x => x.Reject()));
        admin.MapGet("/budgets/{id:guid}/variance", async (Guid id, OperationsStore s, CancellationToken ct) =>
        {
            var b = await s.Budgets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (b is null) return Results.NotFound();
            var lines = await s.BudgetLines.AsNoTracking().Where(x => x.BudgetId == id).ToListAsync(ct);
            var actualRows = await (from l in s.JournalLines.AsNoTracking() join e in s.JournalEntries.AsNoTracking() on new { l.OrganizationId, Id = l.EntryId } equals new { e.OrganizationId, e.Id } join a in s.ChartOfAccounts.AsNoTracking() on new { l.OrganizationId, Id = l.AccountId } equals new { a.OrganizationId, a.Id } where l.PropertyId == b.PropertyId && e.EntryDate.Year == b.Year select new { l.AccountId, a.Code, a.Name, Month = e.EntryDate.Month, l.Debit, l.Credit, a.Type }).ToListAsync(ct);
            var actuals = actualRows.GroupBy(x => new { x.AccountId, x.Code, x.Name, x.Month }).Select(g => new { g.Key.AccountId, g.Key.Code, g.Key.Name, g.Key.Month, Actual = g.First().Type == AccountType.Expense ? g.Sum(x => x.Debit - x.Credit) : g.Sum(x => x.Credit - x.Debit) }).ToList();
            var rows = lines.Select(x => { var actual = actuals.Where(a => a.AccountId == x.AccountId && a.Month == x.Month).Select(a => a.Actual).FirstOrDefault(); return new { x.AccountId, x.Month, Budget = x.Amount, Actual = actual, Variance = actual - x.Amount }; });
            return Results.Ok(new { budgetId = id, propertyId = b.PropertyId, year = b.Year, rows });
        });
        admin.MapPost("/owners", async (OwnerRequest r, OperationsStore s, CancellationToken ct) => { try { var o = new Owner(s.OrganizationId, Guid.NewGuid(), r.Name, r.Email); s.Owners.Add(o); await s.SaveChangesAsync(ct); return Results.Created($"/api/owner-accounting/owners/{o.Id}", o); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });
        admin.MapPost("/ownerships", async (OwnershipRequest r, OperationsStore s, CancellationToken ct) => { if (!await s.Owners.AnyAsync(x => x.Id == r.OwnerId, ct) || !await s.Properties.AnyAsync(x => x.Id == r.PropertyId, ct)) return Results.NotFound(); var total = await s.PropertyOwnerships.Where(x => x.PropertyId == r.PropertyId && x.OwnerId != r.OwnerId).SumAsync(x => (decimal?)x.Percentage, ct) ?? 0; if (total + r.Percentage > 100) return Results.Conflict("Ownership percentages cannot exceed 100."); try { var o = new PropertyOwnership(s.OrganizationId, Guid.NewGuid(), r.OwnerId, r.PropertyId, r.Percentage); s.PropertyOwnerships.Add(o); await s.SaveChangesAsync(ct); return Results.Created($"/api/owner-accounting/ownerships/{o.Id}", o); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });
        admin.MapPost("/management-fees", async (FeeRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct) => { if (!await s.Properties.AnyAsync(x => x.Id == r.PropertyId, ct)) return Results.NotFound(); try { var f = new ManagementFeeRule(s.OrganizationId, Guid.NewGuid(), r.PropertyId, r.Percentage, r.Minimum, clock.GetUtcNow()); s.ManagementFeeRules.Add(f); await s.SaveChangesAsync(ct); return Results.Created($"/api/owner-accounting/management-fees/{f.Id}", f); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });
        admin.MapPost("/distributions", async (DistributionRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct) => { if (!await s.PropertyOwnerships.AnyAsync(x => x.OwnerId == r.OwnerId && x.PropertyId == r.PropertyId, ct)) return Results.BadRequest("Owner is not assigned to the property."); try { var d = new Distribution(s.OrganizationId, Guid.NewGuid(), r.OwnerId, r.PropertyId, r.Amount, r.PaidOn, clock.GetUtcNow()); s.Distributions.Add(d); await s.SaveChangesAsync(ct); return Results.Created($"/api/owner-accounting/distributions/{d.Id}", d); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });
        admin.MapPost("/statements", GenerateStatement);
        var reader = app.MapGroup("/api/owner-accounting").RequireAuthorization(Capabilities.ReadOwnerAccounting);
        reader.MapGet("/statements", async (Guid? ownerId, Guid? propertyId, OperationsStore s, CancellationToken ct) => Results.Ok(await s.OwnerStatements.AsNoTracking().Where(x => ownerId == null || x.OwnerId == ownerId).Where(x => propertyId == null || x.PropertyId == propertyId).OrderByDescending(x => x.EndsOn).ToListAsync(ct)));
        reader.MapGet("/statements/{id:guid}", async (Guid id, OperationsStore s, CancellationToken ct) => { var x = await s.OwnerStatements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); return x is null ? Results.NotFound() : Results.Ok(x); });
    }

    private static async Task<IResult> GenerateStatement(StatementRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct)
    {
        var ownership = await s.PropertyOwnerships.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == r.OwnerId && x.PropertyId == r.PropertyId, ct); if (ownership is null) return Results.BadRequest("Owner is not assigned to the property.");
        var existing = await s.OwnerStatements.SingleOrDefaultAsync(x => x.OwnerId == r.OwnerId && x.PropertyId == r.PropertyId && x.StartsOn == r.StartsOn && x.EndsOn == r.EndsOn, ct); if (existing is not null) return Results.Ok(existing);
        var rows = await (from l in s.JournalLines.AsNoTracking() join e in s.JournalEntries.AsNoTracking() on new { l.OrganizationId, Id = l.EntryId } equals new { e.OrganizationId, e.Id } join a in s.ChartOfAccounts.AsNoTracking() on new { l.OrganizationId, Id = l.AccountId } equals new { a.OrganizationId, a.Id } where l.PropertyId == r.PropertyId && e.EntryDate >= r.StartsOn && e.EntryDate <= r.EndsOn select new { EntryId = e.Id, LineId = l.Id, l.Debit, l.Credit, a.Type }).ToListAsync(ct);
        var income = rows.Where(x => x.Type == AccountType.Revenue).Sum(x => x.Credit - x.Debit) * ownership.Percentage / 100m; var expenses = rows.Where(x => x.Type == AccountType.Expense).Sum(x => x.Debit - x.Credit) * ownership.Percentage / 100m; var net = Math.Max(0, income - expenses); var fee = await s.ManagementFeeRules.AsNoTracking().Where(x => x.PropertyId == r.PropertyId).Select(x => (decimal?)x.Minimum).FirstOrDefaultAsync(ct) ?? 0; var distribution = await s.Distributions.AsNoTracking().Where(x => x.OwnerId == r.OwnerId && x.PropertyId == r.PropertyId && x.PaidOn >= r.StartsOn && x.PaidOn <= r.EndsOn).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var rule = await s.ManagementFeeRules.AsNoTracking().SingleOrDefaultAsync(x => x.PropertyId == r.PropertyId, ct); if (rule is not null) fee = Math.Max(fee, net * rule.Percentage / 100m);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{r.OwnerId:N}|{r.PropertyId:N}|{r.StartsOn:O}|{r.EndsOn:O}|{income:F2}|{expenses:F2}|{fee:F2}|{distribution:F2}")));
        var statement = new OwnerStatement(s.OrganizationId, Guid.NewGuid(), r.OwnerId, r.PropertyId, r.StartsOn, r.EndsOn, income, expenses, fee, distribution, hash, clock.GetUtcNow()); s.OwnerStatements.Add(statement); await s.SaveChangesAsync(ct); return Results.Created($"/api/owner-accounting/statements/{statement.Id}", statement);
    }
    private static async Task<IResult> ChangeBudget(Guid id, OperationsStore s, CancellationToken ct, Action<Budget> change) { var b = await s.Budgets.SingleOrDefaultAsync(x => x.Id == id, ct); if (b is null) return Results.NotFound(); try { change(b); await s.SaveChangesAsync(ct); return Results.Ok(b); } catch (InvalidOperationException e) { return Results.Conflict(e.Message); } catch (ArgumentException e) { return Results.BadRequest(e.Message); } }
}
public sealed record BudgetRequest(Guid PropertyId, int Year, string Name, IReadOnlyList<BudgetLineRequest>? Lines);
public sealed record BudgetLineRequest(Guid AccountId, int Month, decimal Amount);
public sealed record OwnerRequest(string Name, string Email);
public sealed record OwnershipRequest(Guid OwnerId, Guid PropertyId, decimal Percentage);
public sealed record FeeRequest(Guid PropertyId, decimal Percentage, decimal Minimum);
public sealed record DistributionRequest(Guid OwnerId, Guid PropertyId, decimal Amount, DateOnly PaidOn);
public sealed record StatementRequest(Guid OwnerId, Guid PropertyId, DateOnly StartsOn, DateOnly EndsOn);
