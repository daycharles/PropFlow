using PropFlow.Domain.Accounting;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ChartOfAccountTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ChartOfAccount Account(string code = "1000", string name = "Operating cash", AccountType type = AccountType.Asset) =>
        new(Organization, Guid.NewGuid(), code, name, type, Now);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Account_refuses_a_blank_code_or_name(string blank)
    {
        Assert.Throws<ArgumentException>(() => Account(code: blank));
        Assert.Throws<ArgumentException>(() => Account(name: blank));
    }

    [Fact]
    public void Account_refuses_an_over_long_code_or_name()
    {
        Assert.Throws<ArgumentException>(() => Account(code: new string('9', 21)));
        Assert.Throws<ArgumentException>(() => Account(name: new string('x', 201)));
    }

    [Fact]
    public void Account_trims_its_code_and_name_and_starts_active()
    {
        var account = Account(code: "  1000  ", name: "  Operating cash  ");
        Assert.Equal("1000", account.Code);
        Assert.Equal("Operating cash", account.Name);
        Assert.True(account.IsActive);
    }

    [Fact]
    public void Account_refuses_an_unknown_account_type()
    {
        Assert.Throws<ArgumentException>(() => Account(type: (AccountType)99));
    }

    [Fact]
    public void Deactivating_twice_is_refused_and_so_is_activating_an_active_account()
    {
        var account = Account();
        Assert.Throws<InvalidOperationException>(account.Activate);
        account.Deactivate();
        Assert.False(account.IsActive);
        Assert.Throws<InvalidOperationException>(account.Deactivate);
        account.Activate();
        Assert.True(account.IsActive);
    }

    [Theory]
    [InlineData(AccountType.Asset, true)]
    [InlineData(AccountType.Expense, true)]
    [InlineData(AccountType.Liability, false)]
    [InlineData(AccountType.Equity, false)]
    [InlineData(AccountType.Revenue, false)]
    public void Assets_and_expenses_are_debit_normal(AccountType type, bool debitNormal)
    {
        Assert.Equal(debitNormal, Account(type: type).IsDebitNormal);
    }
}
