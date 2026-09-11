using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Accounting;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// FS-S09 general ledger: chart of accounts, fiscal periods, balanced journal entries, reversals,
// period close, trial balance and export.
//
// The two rules that shape this file live in the domain, not here:
//   * JournalEntry.Post refuses to construct an unbalanced entry, so the endpoint never has to
//     check the balance itself — it only translates the refusal into a status code.
//   * Posting into a closed FiscalPeriod raises InvalidOperationException, which maps to 409;
//     a malformed entry raises ArgumentException, which maps to 400.
public static class AccountingEndpoints
{
    public static void MapAccountingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/accounting").RequireAuthorization(Capabilities.ManageAccounting);

        group.MapGet("/accounts", async (bool? activeOnly, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.ChartOfAccounts.AsNoTracking()
                .Where(x => activeOnly != true || x.IsActive).OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Name, x.Type, x.IsActive, x.CreatedAt }).ToListAsync(ct)));

        group.MapPost("/accounts", async (AccountRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var code = request.Code?.Trim() ?? "";
            if (await store.ChartOfAccounts.AnyAsync(x => x.Code == code, ct))
                return Results.Conflict("An account with that code already exists.");
            try
            {
                var account = new ChartOfAccount(store.OrganizationId, Guid.NewGuid(), code, request.Name, request.Type, clock.GetUtcNow());
                store.ChartOfAccounts.Add(account);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/accounting/accounts/{account.Id}", Project(account));
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        });

        // Bulk import with all-or-nothing validation: a batch that names a duplicate code, repeats
        // a code within itself, or carries an invalid row is rejected whole rather than part-applied.
        group.MapPost("/accounts/import", async (AccountImportRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var rows = request.Accounts;
            if (rows is null || rows.Count == 0) return Results.Problem(statusCode: 400, title: "The import contains no accounts");
            if (rows.Count > 500) return Results.Problem(statusCode: 400, title: "An import carries at most 500 accounts");
            var codes = rows.Select(x => x.Code?.Trim() ?? "").ToArray();
            if (codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Length)
                return Results.Problem(statusCode: 400, title: "The import repeats an account code");
            var existing = await store.ChartOfAccounts.AsNoTracking().Where(x => codes.Contains(x.Code)).Select(x => x.Code).ToListAsync(ct);
            if (existing.Count > 0)
                return Results.Conflict($"These account codes already exist: {string.Join(", ", existing.Order())}.");
            try
            {
                var now = clock.GetUtcNow();
                var accounts = rows.Select(x => new ChartOfAccount(store.OrganizationId, Guid.NewGuid(), x.Code, x.Name, x.Type, now)).ToArray();
                store.ChartOfAccounts.AddRange(accounts);
                await store.SaveChangesAsync(ct);
                return Results.Ok(new { imported = accounts.Length, accounts = accounts.Select(Project) });
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        });

        group.MapPost("/accounts/{id:guid}/activate", (Guid id, OperationsStore store, CancellationToken ct) =>
            ChangeAccount(id, store, ct, account => account.Activate()));
        group.MapPost("/accounts/{id:guid}/deactivate", (Guid id, OperationsStore store, CancellationToken ct) =>
            ChangeAccount(id, store, ct, account => account.Deactivate()));

        group.MapGet("/periods", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.FiscalPeriods.AsNoTracking().OrderBy(x => x.StartsOn)
                .Select(x => new { x.Id, x.Name, x.StartsOn, x.EndsOn, x.Status, x.ClosedBy, x.ClosedAt }).ToListAsync(ct)));

        group.MapPost("/periods", async (FiscalPeriodRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (await store.FiscalPeriods.AnyAsync(x => x.StartsOn <= request.EndsOn && x.EndsOn >= request.StartsOn, ct))
                return Results.Conflict("The fiscal period overlaps an existing period.");
            try
            {
                var period = new FiscalPeriod(store.OrganizationId, Guid.NewGuid(), request.Name, request.StartsOn, request.EndsOn);
                store.FiscalPeriods.Add(period);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/accounting/periods/{period.Id}", Project(period));
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        });

        group.MapPost("/periods/{id:guid}/close", async (Guid id, ClaimsPrincipal user, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var period = await store.FiscalPeriods.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (period is null) return Results.NotFound();
            try
            {
                period.Close(WorkScopeAccess.Actor(user), clock.GetUtcNow());
                await store.SaveChangesAsync(ct);
                return Results.Ok(Project(period));
            }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        });

        group.MapGet("/journal", async (Guid? periodId, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.JournalEntries.AsNoTracking()
                .Where(x => periodId == null || x.PeriodId == periodId)
                .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.PostedAt).Take(500)
                .Select(x => new { x.Id, x.PeriodId, x.EntryDate, x.Reference, x.Memo, x.Total, x.PostedBy, x.PostedAt, x.ReversalOfId })
                .ToListAsync(ct)));

        group.MapGet("/journal/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var entry = await store.JournalEntries.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
            return entry is null ? Results.NotFound() : Results.Ok(Project(entry));
        });

        group.MapPost("/journal", async (JournalEntryRequest request, ClaimsPrincipal user, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            if (request.Lines is null || request.Lines.Count < 2)
                return Results.Problem(statusCode: 400, title: "A journal entry needs at least two lines");
            var period = await store.FiscalPeriods.SingleOrDefaultAsync(x => x.Id == request.PeriodId, ct);
            if (period is null) return Results.Problem(statusCode: 400, title: "Fiscal period was not found");
            var accountIds = request.Lines.Select(x => x.AccountId).Distinct().ToArray();
            var accounts = await store.ChartOfAccounts.AsNoTracking().Where(x => accountIds.Contains(x.Id))
                .Select(x => new { x.Id, x.IsActive }).ToListAsync(ct);
            if (accounts.Count != accountIds.Length) return Results.Problem(statusCode: 400, title: "One or more accounts were not found");
            if (accounts.Any(x => !x.IsActive)) return Results.Problem(statusCode: 400, title: "A journal entry cannot post to an inactive account");
            var id = Guid.NewGuid();
            try
            {
                var lines = request.Lines
                    .Select(x => new JournalLine(store.OrganizationId, Guid.NewGuid(), id, x.AccountId, x.Debit, x.Credit, x.Memo)).ToArray();
                var entry = JournalEntry.Post(store.OrganizationId, id, period, request.EntryDate, request.Reference,
                    request.Memo, WorkScopeAccess.Actor(user), clock.GetUtcNow(), lines);
                store.JournalEntries.Add(entry);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/accounting/journal/{entry.Id}", Project(entry));
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        });

        group.MapPost("/journal/{id:guid}/reverse", async (Guid id, ReverseJournalEntryRequest request, ClaimsPrincipal user,
            OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var original = await store.JournalEntries.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (original is null) return Results.NotFound();
            // The unique index on (OrganizationId, ReversalOfId) is the real control; this read
            // turns the race loser's constraint violation into a readable 409 for the common case.
            if (await store.JournalEntries.AnyAsync(x => x.ReversalOfId == id, ct))
                return Results.Problem(statusCode: 409, title: "The entry has already been reversed.");
            var period = await store.FiscalPeriods.SingleOrDefaultAsync(x => x.Id == request.PeriodId, ct);
            if (period is null) return Results.Problem(statusCode: 400, title: "Fiscal period was not found");
            try
            {
                var reversal = original.Reverse(Guid.NewGuid(), period, request.EntryDate, WorkScopeAccess.Actor(user), clock.GetUtcNow());
                store.JournalEntries.Add(reversal);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/accounting/journal/{reversal.Id}", Project(reversal));
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        });

        group.MapGet("/trial-balance", async (Guid? periodId, OperationsStore store, CancellationToken ct) =>
        {
            var rows = await TrialBalanceAsync(periodId, store, ct);
            return Results.Ok(new
            {
                periodId,
                accounts = rows,
                totalDebits = rows.Sum(x => x.Debits),
                totalCredits = rows.Sum(x => x.Credits),
                // Every entry balances individually, so the ledger balances in aggregate. This is
                // the assertion an accountant actually runs; surfacing it makes a broken import
                // visible rather than implied.
                isBalanced = rows.Sum(x => x.Debits) == rows.Sum(x => x.Credits)
            });
        });

        // Export: the full posted detail for a period, in one document, with the same balance
        // assertion attached so a consumer can validate what it received.
        group.MapGet("/export", async (Guid periodId, OperationsStore store, CancellationToken ct) =>
        {
            var period = await store.FiscalPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId, ct);
            if (period is null) return Results.NotFound();
            var entries = await store.JournalEntries.AsNoTracking().Include(x => x.Lines)
                .Where(x => x.PeriodId == periodId).OrderBy(x => x.EntryDate).ThenBy(x => x.PostedAt).ToListAsync(ct);
            var rows = await TrialBalanceAsync(periodId, store, ct);
            return Results.Ok(new
            {
                period = Project(period),
                entries = entries.Select(Project),
                trialBalance = rows,
                totalDebits = rows.Sum(x => x.Debits),
                totalCredits = rows.Sum(x => x.Credits),
                isBalanced = rows.Sum(x => x.Debits) == rows.Sum(x => x.Credits)
            });
        });

        // AP/AR are first-class documents. Account types alone cannot carry invoice identity,
        // due dates, settlement state, or tenant-scoped operational workflow.
        group.MapGet("/payables", async (OperationsStore store, CancellationToken ct) => Results.Ok(await store.PayableInvoices.AsNoTracking().OrderBy(x => x.DueOn).Take(500).ToListAsync(ct)));
        group.MapPost("/payables", async (PayableRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) => { if (!await store.Vendors.AnyAsync(x => x.Id == request.VendorId, ct)) return Results.NotFound(); try { var item = new PayableInvoice(store.OrganizationId, Guid.NewGuid(), request.VendorId, request.InvoiceNumber, request.Amount, request.DueOn, clock.GetUtcNow()); store.PayableInvoices.Add(item); await store.SaveChangesAsync(ct); return Results.Created($"/api/accounting/payables/{item.Id}", item); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });
        group.MapPost("/payables/{id:guid}/pay", async (Guid id, InvoicePaymentRequest request, OperationsStore store, CancellationToken ct) => await ChangeInvoice(id, store, ct, x => x.Pay(request.Amount)));
        group.MapPost("/payables/{id:guid}/void", async (Guid id, OperationsStore store, CancellationToken ct) => await ChangeInvoice(id, store, ct, x => x.Void()));
        group.MapGet("/receivables", async (OperationsStore store, CancellationToken ct) => Results.Ok(await store.ReceivableInvoices.AsNoTracking().OrderBy(x => x.DueOn).Take(500).ToListAsync(ct)));
        group.MapPost("/receivables", async (ReceivableRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) => { if (!await store.Residents.AnyAsync(x => x.Id == request.ResidentId, ct)) return Results.NotFound(); try { var item = new ReceivableInvoice(store.OrganizationId, Guid.NewGuid(), request.ResidentId, request.InvoiceNumber, request.Amount, request.DueOn, clock.GetUtcNow()); store.ReceivableInvoices.Add(item); await store.SaveChangesAsync(ct); return Results.Created($"/api/accounting/receivables/{item.Id}", item); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });
        group.MapPost("/receivables/{id:guid}/receive", async (Guid id, InvoicePaymentRequest request, OperationsStore store, CancellationToken ct) => await ChangeReceivable(id, store, ct, x => x.Receive(request.Amount)));
        group.MapPost("/receivables/{id:guid}/void", async (Guid id, OperationsStore store, CancellationToken ct) => await ChangeReceivable(id, store, ct, x => x.Void()));
        group.MapGet("/bank-accounts", async (OperationsStore store, CancellationToken ct) => Results.Ok(await store.BankAccounts.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)));
        group.MapPost("/bank-accounts", async (BankAccountRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) => { if (!await store.ChartOfAccounts.AnyAsync(x => x.Id == request.AssetAccountId && x.Type == AccountType.Asset, ct)) return Results.Problem(statusCode: 400, title: "An active asset account is required."); try { var item = new BankAccount(store.OrganizationId, Guid.NewGuid(), request.Name, request.Institution, request.LastFour, request.AssetAccountId, clock.GetUtcNow()); store.BankAccounts.Add(item); await store.SaveChangesAsync(ct); return Results.Created($"/api/accounting/bank-accounts/{item.Id}", item); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });
        group.MapGet("/bank-accounts/{id:guid}/transactions", async (Guid id, OperationsStore store, CancellationToken ct) => Results.Ok(await store.BankTransactions.AsNoTracking().Where(x => x.BankAccountId == id).OrderByDescending(x => x.PostedOn).Take(500).ToListAsync(ct)));
        group.MapPost("/bank-accounts/{id:guid}/transactions", async (Guid id, BankTransactionRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) => { if (!await store.BankAccounts.AnyAsync(x => x.Id == id, ct)) return Results.NotFound(); try { var item = new BankTransaction(store.OrganizationId, Guid.NewGuid(), id, request.ExternalId, request.Amount, request.PostedOn, clock.GetUtcNow()); store.BankTransactions.Add(item); await store.SaveChangesAsync(ct); return Results.Created($"/api/accounting/bank-accounts/{id}/transactions/{item.Id}", item); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });
        group.MapPost("/bank-transactions/{id:guid}/match", async (Guid id, MatchBankTransactionRequest request, OperationsStore store, CancellationToken ct) => { var item = await store.BankTransactions.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Results.NotFound(); if (!await store.JournalEntries.AnyAsync(x => x.Id == request.JournalEntryId, ct)) return Results.BadRequest(); item.Match(request.JournalEntryId); await store.SaveChangesAsync(ct); return Results.Ok(item); });
    }

    private static async Task<IResult> ChangeAccount(Guid id, OperationsStore store, CancellationToken ct, Action<ChartOfAccount> change)
    {
        var account = await store.ChartOfAccounts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (account is null) return Results.NotFound();
        try { change(account); await store.SaveChangesAsync(ct); return Results.Ok(Project(account)); }
        catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
    }

    private static async Task<IResult> ChangeInvoice(Guid id, OperationsStore store, CancellationToken ct, Action<PayableInvoice> change) { var item = await store.PayableInvoices.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Results.NotFound(); try { change(item); await store.SaveChangesAsync(ct); return Results.Ok(item); } catch (InvalidOperationException e) { return Results.Conflict(e.Message); } catch (ArgumentOutOfRangeException e) { return Results.Problem(statusCode: 400, title: e.Message); } }
    private static async Task<IResult> ChangeReceivable(Guid id, OperationsStore store, CancellationToken ct, Action<ReceivableInvoice> change) { var item = await store.ReceivableInvoices.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Results.NotFound(); try { change(item); await store.SaveChangesAsync(ct); return Results.Ok(item); } catch (InvalidOperationException e) { return Results.Conflict(e.Message); } catch (ArgumentOutOfRangeException e) { return Results.Problem(statusCode: 400, title: e.Message); } }

    private static async Task<List<TrialBalanceRow>> TrialBalanceAsync(Guid? periodId, OperationsStore store, CancellationToken ct)
    {
        var totals = await (
            from line in store.JournalLines.AsNoTracking()
            join entry in store.JournalEntries.AsNoTracking()
                on new { line.OrganizationId, Id = line.EntryId } equals new { entry.OrganizationId, entry.Id }
            join account in store.ChartOfAccounts.AsNoTracking()
                on new { line.OrganizationId, Id = line.AccountId } equals new { account.OrganizationId, account.Id }
            where periodId == null || entry.PeriodId == periodId
            group line by new { AccountId = account.Id, account.Code, account.Name, account.Type } into grouped
            select new
            {
                grouped.Key.AccountId, grouped.Key.Code, grouped.Key.Name, grouped.Key.Type,
                Debits = grouped.Sum(x => x.Debit), Credits = grouped.Sum(x => x.Credit)
            }).ToListAsync(ct);

        // Signing the balance needs ChartOfAccount.IsDebitNormal, which is a computed property and
        // therefore not translatable — the arithmetic happens here, after the aggregation.
        return totals.OrderBy(x => x.Code, StringComparer.Ordinal).Select(x => new TrialBalanceRow(
            x.AccountId, x.Code, x.Name, x.Type, x.Debits, x.Credits,
            x.Type is AccountType.Asset or AccountType.Expense ? x.Debits - x.Credits : x.Credits - x.Debits)).ToList();
    }

    private static object Project(ChartOfAccount account) =>
        new { account.Id, account.Code, account.Name, account.Type, account.IsActive, account.CreatedAt };

    private static object Project(FiscalPeriod period) =>
        new { period.Id, period.Name, period.StartsOn, period.EndsOn, period.Status, period.ClosedBy, period.ClosedAt };

    private static object Project(JournalEntry entry) => new
    {
        entry.Id, entry.PeriodId, entry.EntryDate, entry.Reference, entry.Memo, entry.Total,
        entry.PostedBy, entry.PostedAt, entry.ReversalOfId,
        Lines = entry.Lines.Select(line => new { line.Id, line.AccountId, line.Debit, line.Credit, line.Memo })
    };
}

public sealed record AccountRequest(string Code, string Name, AccountType Type);
public sealed record AccountImportRequest(IReadOnlyList<AccountRequest> Accounts);
public sealed record FiscalPeriodRequest(string Name, DateOnly StartsOn, DateOnly EndsOn);
public sealed record JournalLineRequest(Guid AccountId, decimal Debit, decimal Credit, string? Memo);
public sealed record JournalEntryRequest(Guid PeriodId, DateOnly EntryDate, string Reference, string? Memo, IReadOnlyList<JournalLineRequest> Lines);
public sealed record ReverseJournalEntryRequest(Guid PeriodId, DateOnly EntryDate);
public sealed record TrialBalanceRow(Guid AccountId, string Code, string Name, AccountType Type, decimal Debits, decimal Credits, decimal Balance);
public sealed record PayableRequest(Guid VendorId, string InvoiceNumber, decimal Amount, DateOnly DueOn);
public sealed record ReceivableRequest(Guid ResidentId, string InvoiceNumber, decimal Amount, DateOnly DueOn);
public sealed record InvoicePaymentRequest(decimal Amount);
public sealed record BankAccountRequest(string Name, string Institution, string LastFour, Guid AssetAccountId);
public sealed record BankTransactionRequest(string ExternalId, decimal Amount, DateOnly PostedOn);
public sealed record MatchBankTransactionRequest(Guid JournalEntryId);
