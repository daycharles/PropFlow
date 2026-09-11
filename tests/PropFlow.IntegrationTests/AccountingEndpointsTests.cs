using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PropFlow.IntegrationTests;

// FS-S09 acceptance, over HTTP: every journal balances, closed periods reject mutation, a
// correction is an audited reversing entry, import/export validate, and the ledger is reachable
// only by a role holding Accounting.Manage.
[Collection("PostgreSQL")]
public sealed class AccountingEndpointsTests(DatabaseFixture fixture)
{
    private static async Task<Guid> CreatePeriodAsync(Scenario s, string name = "2026-01",
        string startsOn = "2026-01-01", string endsOn = "2026-01-31")
    {
        using var response = await s.Client.PostAsJsonAsync("/api/accounting/periods", new { name, startsOn, endsOn });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateAccountAsync(Scenario s, string code, string name, string type)
    {
        using var response = await s.Client.PostAsJsonAsync("/api/accounting/accounts", new { code, name, type });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object Entry(Guid periodId, Guid debitAccount, Guid creditAccount, decimal debit, decimal credit,
        string reference = "JE-1001", string entryDate = "2026-01-15") => new
        {
            periodId,
            entryDate,
            reference,
            memo = "January rent",
            lines = new object[]
            {
                new { accountId = debitAccount, debit, credit = 0m, memo = "Cash in" },
                new { accountId = creditAccount, debit = 0m, credit, memo = "Rent earned" }
            }
        };

    [Fact]
    public async Task A_balanced_journal_entry_posts_and_reads_back_with_its_lines()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var period = await CreatePeriodAsync(s);
        var cash = await CreateAccountAsync(s, "1000", "Operating cash", "Asset");
        var revenue = await CreateAccountAsync(s, "4000", "Rent revenue", "Revenue");

        using var post = await s.Client.PostAsJsonAsync("/api/accounting/journal", Entry(period, cash, revenue, 1650m, 1650m));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        var entryId = created.GetProperty("id").GetGuid();
        Assert.Equal(1650m, created.GetProperty("total").GetDecimal());
        Assert.Equal(s.AdminA, created.GetProperty("postedBy").GetGuid());

        using var read = await s.Client.GetAsync($"/api/accounting/journal/{entryId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var entry = await read.Content.ReadFromJsonAsync<JsonElement>();
        var lines = entry.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal(lines.Sum(x => x.GetProperty("debit").GetDecimal()), lines.Sum(x => x.GetProperty("credit").GetDecimal()));
    }

    [Fact]
    public async Task An_unbalanced_journal_entry_is_refused_and_nothing_is_written()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var period = await CreatePeriodAsync(s);
        var cash = await CreateAccountAsync(s, "1000", "Operating cash", "Asset");
        var revenue = await CreateAccountAsync(s, "4000", "Rent revenue", "Revenue");

        using var post = await s.Client.PostAsJsonAsync("/api/accounting/journal", Entry(period, cash, revenue, 1650m, 1600m));
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Contains("must balance", await post.Content.ReadAsStringAsync());

        await using var store = s.Store(s.OrganizationA);
        Assert.Empty(await store.JournalEntries.ToListAsync());
        Assert.Empty(await store.JournalLines.ToListAsync());
    }

    [Fact]
    public async Task A_closed_period_rejects_posting_with_409_and_closing_twice_is_also_409()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var period = await CreatePeriodAsync(s);
        var cash = await CreateAccountAsync(s, "1000", "Operating cash", "Asset");
        var revenue = await CreateAccountAsync(s, "4000", "Rent revenue", "Revenue");

        using var close = await s.Client.PostAsync($"/api/accounting/periods/{period}/close", null);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var closed = await close.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.Equal(s.AdminA, closed.GetProperty("closedBy").GetGuid());

        using var post = await s.Client.PostAsJsonAsync("/api/accounting/journal", Entry(period, cash, revenue, 100m, 100m));
        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);

        using var closeAgain = await s.Client.PostAsync($"/api/accounting/periods/{period}/close", null);
        Assert.Equal(HttpStatusCode.Conflict, closeAgain.StatusCode);

        await using var store = s.Store(s.OrganizationA);
        Assert.Empty(await store.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task Reversing_an_entry_writes_an_audited_mirror_and_leaves_the_original_untouched()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var period = await CreatePeriodAsync(s);
        var cash = await CreateAccountAsync(s, "1000", "Operating cash", "Asset");
        var revenue = await CreateAccountAsync(s, "4000", "Rent revenue", "Revenue");
        using var post = await s.Client.PostAsJsonAsync("/api/accounting/journal", Entry(period, cash, revenue, 1650m, 1650m));
        var original = (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var reverse = await s.Client.PostAsJsonAsync($"/api/accounting/journal/{original}/reverse",
            new { periodId = period, entryDate = "2026-01-20" });
        Assert.Equal(HttpStatusCode.Created, reverse.StatusCode);
        var reversal = await reverse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(original, reversal.GetProperty("reversalOfId").GetGuid());
        Assert.StartsWith("REV-", reversal.GetProperty("reference").GetString());

        await using var store = s.Store(s.OrganizationA);
        var entries = await store.JournalEntries.AsNoTracking().Include(x => x.Lines).OrderBy(x => x.EntryDate).ToListAsync();
        Assert.Equal(2, entries.Count);
        var kept = entries.Single(x => x.Id == original);
        var mirror = entries.Single(x => x.ReversalOfId == original);
        // The original is unchanged: the correction is a second row, never an edit.
        Assert.Equal("JE-1001", kept.Reference);
        Assert.Equal(1650m, kept.Lines.Single(x => x.AccountId == cash).Debit);
        Assert.Equal(1650m, mirror.Lines.Single(x => x.AccountId == cash).Credit);
        Assert.Equal(0m, mirror.Lines.Single(x => x.AccountId == cash).Debit);
        // ...and the pair nets to nothing.
        Assert.Equal(entries.SelectMany(x => x.Lines).Sum(x => x.Debit), entries.SelectMany(x => x.Lines).Sum(x => x.Credit));

        using var again = await s.Client.PostAsJsonAsync($"/api/accounting/journal/{original}/reverse",
            new { periodId = period, entryDate = "2026-01-21" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_journal_entry_cannot_post_to_an_inactive_or_unknown_account()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var period = await CreatePeriodAsync(s);
        var cash = await CreateAccountAsync(s, "1000", "Operating cash", "Asset");
        var revenue = await CreateAccountAsync(s, "4000", "Rent revenue", "Revenue");
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/accounting/accounts/{revenue}/deactivate", null)).StatusCode);

        using var inactive = await s.Client.PostAsJsonAsync("/api/accounting/journal", Entry(period, cash, revenue, 10m, 10m));
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("inactive account", await inactive.Content.ReadAsStringAsync());

        using var unknown = await s.Client.PostAsJsonAsync("/api/accounting/journal", Entry(period, cash, Guid.NewGuid(), 10m, 10m));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Account_import_validates_the_batch_and_applies_all_or_nothing()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();

        using var repeated = await s.Client.PostAsJsonAsync("/api/accounting/accounts/import", new
        {
            accounts = new object[]
            {
                new { code = "1000", name = "Operating cash", type = "Asset" },
                new { code = "1000", name = "Duplicate", type = "Asset" }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);

        using var invalid = await s.Client.PostAsJsonAsync("/api/accounting/accounts/import", new
        {
            accounts = new object[]
            {
                new { code = "1000", name = "Operating cash", type = "Asset" },
                new { code = "", name = "Blank code", type = "Asset" }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        await using (var store = s.Store(s.OrganizationA))
            Assert.Empty(await store.ChartOfAccounts.ToListAsync());

        using var accepted = await s.Client.PostAsJsonAsync("/api/accounting/accounts/import", new
        {
            accounts = new object[]
            {
                new { code = "1000", name = "Operating cash", type = "Asset" },
                new { code = "2000", name = "Security deposits held", type = "Liability" },
                new { code = "4000", name = "Rent revenue", type = "Revenue" }
            }
        });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(3, (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imported").GetInt32());

        // Re-importing the same codes collides with what is already there.
        using var again = await s.Client.PostAsJsonAsync("/api/accounting/accounts/import", new
        {
            accounts = new object[] { new { code = "1000", name = "Operating cash", type = "Asset" } }
        });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Trial_balance_and_export_report_the_period_and_assert_it_balances()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var period = await CreatePeriodAsync(s);
        var cash = await CreateAccountAsync(s, "1000", "Operating cash", "Asset");
        var revenue = await CreateAccountAsync(s, "4000", "Rent revenue", "Revenue");
        using (var first = await s.Client.PostAsJsonAsync("/api/accounting/journal", Entry(period, cash, revenue, 1650m, 1650m)))
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using (var second = await s.Client.PostAsJsonAsync("/api/accounting/journal", Entry(period, cash, revenue, 350m, 350m, "JE-1002", "2026-01-20")))
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var balance = await s.Client.GetFromJsonAsync<JsonElement>($"/api/accounting/trial-balance?periodId={period}");
        Assert.True(balance.GetProperty("isBalanced").GetBoolean());
        Assert.Equal(2000m, balance.GetProperty("totalDebits").GetDecimal());
        Assert.Equal(2000m, balance.GetProperty("totalCredits").GetDecimal());
        var accounts = balance.GetProperty("accounts").EnumerateArray().ToArray();
        Assert.Equal(2, accounts.Length);
        // Cash is debit-normal, rent revenue credit-normal; both report a positive balance.
        Assert.Equal(2000m, accounts.Single(x => x.GetProperty("code").GetString() == "1000").GetProperty("balance").GetDecimal());
        Assert.Equal(2000m, accounts.Single(x => x.GetProperty("code").GetString() == "4000").GetProperty("balance").GetDecimal());

        var export = await s.Client.GetFromJsonAsync<JsonElement>($"/api/accounting/export?periodId={period}");
        Assert.True(export.GetProperty("isBalanced").GetBoolean());
        Assert.Equal(2, export.GetProperty("entries").GetArrayLength());
        Assert.Equal("2026-01", export.GetProperty("period").GetProperty("name").GetString());
        Assert.All(export.GetProperty("entries").EnumerateArray(), entry => Assert.Equal(2, entry.GetProperty("lines").GetArrayLength()));

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/accounting/export?periodId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Overlapping_fiscal_periods_are_refused()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        await CreatePeriodAsync(s);
        using var overlapping = await s.Client.PostAsJsonAsync("/api/accounting/periods",
            new { name = "2026-01-late", startsOn = "2026-01-15", endsOn = "2026-02-15" });
        Assert.Equal(HttpStatusCode.Conflict, overlapping.StatusCode);
        using var inverted = await s.Client.PostAsJsonAsync("/api/accounting/periods",
            new { name = "2026-03", startsOn = "2026-03-31", endsOn = "2026-03-01" });
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
    }

    // The financial-access test: a role without Accounting.Manage cannot read or write any part of
    // the ledger. Read Only holds only Work.Read (Capabilities.ForRole).
    [Fact]
    public async Task A_role_without_the_accounting_capability_is_denied_every_ledger_surface()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);

        foreach (var path in new[] { "/api/accounting/accounts", "/api/accounting/periods", "/api/accounting/journal", "/api/accounting/trial-balance" })
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Client.PostAsJsonAsync("/api/accounting/accounts", new { code = "1000", name = "Cash", type = "Asset" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Client.PostAsJsonAsync("/api/accounting/periods", new { name = "2026-01", startsOn = "2026-01-01", endsOn = "2026-01-31" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Client.PostAsJsonAsync("/api/accounting/journal", new { periodId = Guid.NewGuid(), entryDate = "2026-01-15", reference = "JE-1", lines = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Client.PostAsJsonAsync($"/api/accounting/periods/{Guid.NewGuid()}/close", new { })).StatusCode);

        await using var store = s.Store(s.OrganizationA);
        Assert.Empty(await store.ChartOfAccounts.ToListAsync());
        Assert.Empty(await store.FiscalPeriods.ToListAsync());
    }

    [Fact]
    public async Task The_ledger_is_scoped_to_the_signed_in_organization()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var period = await CreatePeriodAsync(s);
        var cash = await CreateAccountAsync(s, "1000", "Operating cash", "Asset");
        var revenue = await CreateAccountAsync(s, "4000", "Rent revenue", "Revenue");
        using (var post = await s.Client.PostAsJsonAsync("/api/accounting/journal", Entry(period, cash, revenue, 1650m, 1650m)))
            Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        await using var other = s.Store(s.OrganizationB);
        Assert.Empty(await other.JournalEntries.ToListAsync());
        Assert.Empty(await other.ChartOfAccounts.ToListAsync());
        Assert.Empty(await other.FiscalPeriods.ToListAsync());
    }
}
