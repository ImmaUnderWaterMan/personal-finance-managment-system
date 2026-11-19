using PersonalFinance.AccountModule;
using PersonalFinance.BudgetModule;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Reports;
using PersonalFinance.Contracts.Transactions;
using PersonalFinance.ReportModule;
using PersonalFinance.TransactionModule;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public class ReportServiceTests
{
    private readonly Guid user = Guid.NewGuid();
    private readonly AccountService accounts = new(new InMemoryAccountRepository());
    private readonly TransactionService transactions;
    private readonly BudgetService budgets;
    private readonly ReportService reports;
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 12, 1);

    public ReportServiceTests()
    {
        transactions = new(new InMemoryTransactionRepository(), accounts);
        budgets = new(new InMemoryBudgetRepository(), transactions);
        reports = new(accounts, transactions, budgets);
    }

    private async Task Add(Guid owner, Guid account, Guid category, TransactionType type, decimal amount, DateOnly date)
    {
        await transactions.RegisterAsync(owner, new(Guid.NewGuid(), account, category, type, amount, date, null));
    }

    [Fact]
    public async Task Summary_uses_prior_operations_and_excludes_end_boundary()
    {
        var account = await accounts.CreateAsync(user, new("Основной", 1000));
        var expense = await transactions.CreateCategoryAsync(user, new("Продукты", TransactionType.Expense));
        var income = await transactions.CreateCategoryAsync(user, new("Зарплата", TransactionType.Income));
        await Add(user, account.Id, income.Id, TransactionType.Income, 500, From.AddDays(-1));
        await Add(user, account.Id, expense.Id, TransactionType.Expense, 100, From);
        await Add(user, account.Id, income.Id, TransactionType.Income, 200, To.AddDays(-1));
        await Add(user, account.Id, expense.Id, TransactionType.Expense, 900, To);
        var report = await reports.GetSummaryAsync(user, new(From, To));
        Assert.Equal(200m, report.TotalIncome);
        Assert.Equal(100m, report.TotalExpense);
        Assert.Equal(100m, report.NetIncome);
        var balance = Assert.Single(report.Accounts);
        Assert.Equal(1500m, balance.BalanceAtStart);
        Assert.Equal(1600m, balance.BalanceAtEnd);
        Assert.Equal("Продукты", Assert.Single(report.ExpensesByCategory).Name);
    }

    [Fact]
    public async Task Archived_accounts_and_negative_balances_remain_visible()
    {
        var account = await accounts.CreateAsync(user, new("Основной", 0));
        var category = await transactions.CreateCategoryAsync(user, new("Продукты", TransactionType.Expense));
        await Add(user, account.Id, category.Id, TransactionType.Expense, 100, From);
        await accounts.ArchiveAsync(user, account.Id);
        var balance = Assert.Single((await reports.GetSummaryAsync(user, new(From, To))).Accounts);
        Assert.True(balance.IsArchived);
        Assert.Equal(-100m, balance.BalanceAtEnd);
    }

    [Fact]
    public async Task Foreign_data_is_excluded()
    {
        var stranger = Guid.NewGuid();
        var account = await accounts.CreateAsync(stranger, new("Чужой", 1000));
        var category = await transactions.CreateCategoryAsync(stranger, new("Чужая", TransactionType.Expense));
        await Add(stranger, account.Id, category.Id, TransactionType.Expense, 50, From);
        await budgets.SetAsync(stranger, new(category.Id, 2026, 11, 100));
        var report = await reports.GetSummaryAsync(user, new(From, To));
        Assert.Empty(report.Accounts);
        Assert.Empty(report.ExpensesByCategory);
        Assert.Equal(0m, report.TotalExpense);
        Assert.Empty(await reports.GetMonthlyBudgetsAsync(user, 2026, 11));
    }

    [Fact]
    public async Task Empty_history_preserves_opening_balance_without_creating_income()
    {
        await accounts.CreateAsync(user, new("Накопления", 500));
        var report = await reports.GetSummaryAsync(user, new(From, To));
        Assert.Equal(0m, report.TotalIncome);
        Assert.Equal(0m, report.TotalExpense);
        Assert.Equal(500m, Assert.Single(report.Accounts).BalanceAtEnd);
        Assert.Empty(report.ExpensesByCategory);
    }

    [Fact]
    public async Task Monthly_report_includes_only_configured_expense_budgets()
    {
        var account = await accounts.CreateAsync(user, new("Основной", 0));
        var food = await transactions.CreateCategoryAsync(user, new("Продукты", TransactionType.Expense));
        await transactions.CreateCategoryAsync(user, new("Транспорт", TransactionType.Expense));
        await transactions.CreateCategoryAsync(user, new("Зарплата", TransactionType.Income));
        await budgets.SetAsync(user, new(food.Id, 2026, 11, 100));
        await Add(user, account.Id, food.Id, TransactionType.Expense, 120, From);
        var report = Assert.Single(await reports.GetMonthlyBudgetsAsync(user, 2026, 11));
        Assert.Equal("Продукты", report.CategoryName);
        Assert.Equal(-20m, report.Budget.Remaining);
        Assert.Equal(BudgetStatus.Exceeded, report.Budget.Status);
        Assert.Empty(await reports.GetMonthlyBudgetsAsync(user, 2026, 12));
    }

    [Fact]
    public async Task Invalid_period_and_empty_user_are_rejected()
    {
        var period = await Assert.ThrowsAsync<ReportException>(() => reports.GetSummaryAsync(user, new(To, From)));
        Assert.Equal(ReportErrorCodes.InvalidPeriod, period.Code);
        var owner = await Assert.ThrowsAsync<ReportException>(() => reports.GetSummaryAsync(Guid.Empty, new(From, To)));
        Assert.Equal(ReportErrorCodes.InvalidUser, owner.Code);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2026, 13)]
    [InlineData(9999, 12)]
    public async Task Invalid_month_is_rejected_even_without_categories(int year, int month)
    {
        var error = await Assert.ThrowsAsync<ReportException>(() => reports.GetMonthlyBudgetsAsync(user, year, month));
        Assert.Equal(ReportErrorCodes.InvalidPeriod, error.Code);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reports.GetSummaryAsync(user, new(From, To), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reports.GetMonthlyBudgetsAsync(user, 2026, 11, cancellation.Token));
    }

    [Fact]
    public async Task Categories_and_accounts_are_not_merged_by_name()
    {
        var first = await accounts.CreateAsync(user, new("Счёт", 100));
        var second = await accounts.CreateAsync(user, new("Счёт", 200));
        var food1 = await transactions.CreateCategoryAsync(user, new("Еда", TransactionType.Expense));
        var food2 = await transactions.CreateCategoryAsync(user, new("Еда", TransactionType.Expense));
        await Add(user, first.Id, food1.Id, TransactionType.Expense, 10, From);
        await Add(user, second.Id, food2.Id, TransactionType.Expense, 20, From);
        var report = await reports.GetSummaryAsync(user, new(From, To));
        Assert.Equal(2, report.Accounts.Count);
        Assert.Equal(2, report.ExpensesByCategory.Count);
        Assert.Equal(90m, report.Accounts.Single(a => a.AccountId == first.Id).BalanceAtEnd);
        Assert.Equal(180m, report.Accounts.Single(a => a.AccountId == second.Id).BalanceAtEnd);
    }
}
