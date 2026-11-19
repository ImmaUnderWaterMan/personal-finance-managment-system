using PersonalFinance.AccountModule;
using PersonalFinance.BudgetModule;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Transactions;
using PersonalFinance.TransactionModule;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public class BudgetTransactionTests
{
    private readonly Guid user = Guid.NewGuid();
    private readonly AccountService accounts = new(new InMemoryAccountRepository());
    private readonly TransactionService transactions;
    private readonly BudgetService budgets;

    public BudgetTransactionTests()
    {
        transactions = new(new InMemoryTransactionRepository(), accounts);
        budgets = new(new InMemoryBudgetRepository(), transactions);
    }

    [Fact]
    public async Task Budget_recalculates_after_expense_and_excludes_next_month()
    {
        var account = await accounts.CreateAsync(user, new("Основной", 0));
        var category = await transactions.CreateCategoryAsync(user, new("Продукты", TransactionType.Expense));
        await budgets.SetAsync(user, new(category.Id, 2026, 11, 100));
        Assert.Equal(0m, (await budgets.GetAsync(user, category.Id, 2026, 11)).Spent);
        var request = new CreateTransactionRequest(Guid.NewGuid(), account.Id, category.Id, TransactionType.Expense, 120, new(2026, 11, 30), null);
        await transactions.RegisterAsync(user, request);
        await transactions.RegisterAsync(user, request with { RequestId = Guid.NewGuid(), OperationDate = new(2026, 12, 1) });
        var result = await budgets.GetAsync(user, category.Id, 2026, 11);
        Assert.Equal(120m, result.Spent);
        Assert.Equal(-20m, result.Remaining);
        Assert.Equal(BudgetStatus.Exceeded, result.Status);
        await budgets.SetAsync(user, new(category.Id, 2026, 11, 150));
        Assert.Equal(BudgetStatus.WithinLimit, (await budgets.GetAsync(user, category.Id, 2026, 11)).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Foreign_or_missing_category_cannot_receive_budget(bool exists)
    {
        var categoryId = exists
            ? (await transactions.CreateCategoryAsync(Guid.NewGuid(), new("Чужая", TransactionType.Expense))).Id
            : Guid.NewGuid();
        var error = await Assert.ThrowsAsync<BudgetException>(() => budgets.SetAsync(user, new(categoryId, 2026, 11, 100)));
        Assert.Equal(TransactionErrorCodes.CategoryNotFound, error.Code);
    }

    [Fact]
    public async Task Income_category_cannot_receive_expense_budget()
    {
        var category = await transactions.CreateCategoryAsync(user, new("Зарплата", TransactionType.Income));
        var error = await Assert.ThrowsAsync<BudgetException>(() => budgets.SetAsync(user, new(category.Id, 2026, 11, 100)));
        Assert.Equal(TransactionErrorCodes.InvalidCategoryType, error.Code);
    }

    [Fact]
    public async Task Budgets_for_different_months_are_independent()
    {
        var category = await transactions.CreateCategoryAsync(user, new("Продукты", TransactionType.Expense));
        await budgets.SetAsync(user, new(category.Id, 2026, 11, 100));
        await budgets.SetAsync(user, new(category.Id, 2026, 12, 200));
        Assert.Equal(100m, (await budgets.GetAsync(user, category.Id, 2026, 11)).Limit);
        Assert.Equal(200m, (await budgets.GetAsync(user, category.Id, 2026, 12)).Limit);
    }
}
