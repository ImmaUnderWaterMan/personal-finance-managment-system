using PersonalFinance.BudgetModule;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Transactions;
using Xunit;

namespace PersonalFinance.UnitTests;

public class BudgetServiceTests
{
    private readonly Guid user = Guid.NewGuid();
    private readonly Guid category = Guid.NewGuid();

    [Theory]
    [InlineData(0, 100, BudgetStatus.WithinLimit)]
    [InlineData(100, 0, BudgetStatus.WithinLimit)]
    [InlineData(120, -20, BudgetStatus.Exceeded)]
    public async Task Summary_calculates_remaining_and_status(int spent, int remaining, BudgetStatus status)
    {
        var service = new BudgetService(new InMemoryBudgetRepository(), new ExpenseQuery(new(true, spent, null)));
        await service.SetAsync(user, new(category, 2026, 11, 100));
        var result = await service.GetAsync(user, category, 2026, 11);
        Assert.Equal(remaining, result.Remaining);
        Assert.Equal(status, result.Status);
        Assert.Equal(spent, result.Spent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public async Task Invalid_limit_is_not_saved(double limit)
    {
        var repository = new InMemoryBudgetRepository();
        var service = new BudgetService(repository, new ExpenseQuery(new(true, 0, null)));
        var error = await Assert.ThrowsAsync<BudgetException>(() => service.SetAsync(user, new(category, 2026, 11, (decimal)limit)));
        Assert.Equal(BudgetErrorCodes.InvalidLimit, error.Code);
        Assert.Null(await repository.GetAsync(user, category, 2026, 11));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2026, 13)]
    [InlineData(2026, 0)]
    [InlineData(9999, 12)]
    public async Task Invalid_period_is_rejected(int year, int month)
    {
        var service = new BudgetService(new InMemoryBudgetRepository(), new ExpenseQuery(new(true, 0, null)));
        var error = await Assert.ThrowsAsync<BudgetException>(() => service.SetAsync(user, new(category, year, month, 100)));
        Assert.Equal(BudgetErrorCodes.InvalidPeriod, error.Code);
    }

    [Fact]
    public async Task Expense_failure_does_not_overwrite_existing_limit()
    {
        var repository = new InMemoryBudgetRepository();
        var query = new ExpenseQuery(new(true, 0, null));
        var service = new BudgetService(repository, query);
        await service.SetAsync(user, new(category, 2026, 11, 100));
        query.Result = new(false, null, TransactionErrorCodes.CategoryNotFound);
        var error = await Assert.ThrowsAsync<BudgetException>(() => service.SetAsync(user, new(category, 2026, 11, 200)));
        Assert.Equal(TransactionErrorCodes.CategoryNotFound, error.Code);
        Assert.Equal(100m, (await repository.GetAsync(user, category, 2026, 11))!.Limit);
        await Assert.ThrowsAsync<BudgetException>(() => service.GetAsync(user, category, 2026, 11));
    }

    [Fact]
    public async Task Changing_limit_updates_existing_budget()
    {
        var service = new BudgetService(new InMemoryBudgetRepository(), new ExpenseQuery(new(true, 25, null)));
        await service.SetAsync(user, new(category, 2026, 11, 100));
        await service.SetAsync(user, new(category, 2026, 11, 200));
        Assert.Equal(175m, (await service.GetAsync(user, category, 2026, 11)).Remaining);
    }

    [Fact]
    public async Task Cancellation_does_not_save_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var service = new BudgetService(repository, new ExpenseQuery(new(true, 0, null)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetAsync(user, new(category, 2026, 11, 100), cancellation.Token));
        Assert.Null(await repository.GetAsync(user, category, 2026, 11));
    }

    [Fact]
    public async Task Leap_year_month_has_correct_exclusive_end()
    {
        var query = new ExpenseQuery(new(true, 0, null));
        var service = new BudgetService(new InMemoryBudgetRepository(), query);
        await service.SetAsync(user, new(category, 2028, 2, 100));
        Assert.Equal(new DateOnly(2028, 2, 1), query.Request!.From);
        Assert.Equal(new DateOnly(2028, 3, 1), query.Request.ToExclusive);
    }

    [Fact]
    public async Task Foreign_user_cannot_read_budget()
    {
        var service = new BudgetService(new InMemoryBudgetRepository(), new ExpenseQuery(new(true, 0, null)));
        await service.SetAsync(user, new(category, 2026, 11, 100));
        var error = await Assert.ThrowsAsync<BudgetException>(() => service.GetAsync(Guid.NewGuid(), category, 2026, 11));
        Assert.Equal(BudgetErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task Empty_user_is_rejected()
    {
        var service = new BudgetService(new InMemoryBudgetRepository(), new ExpenseQuery(new(true, 0, null)));
        var error = await Assert.ThrowsAsync<BudgetException>(() => service.SetAsync(Guid.Empty, new(category, 2026, 11, 100)));
        Assert.Equal(BudgetErrorCodes.InvalidUser, error.Code);
    }

    private sealed class ExpenseQuery(ExpenseSummaryResult result) : IExpenseQueryService
    {
        public ExpenseSummaryResult Result { get; set; } = result;
        public ExpenseSummaryRequest? Request { get; private set; }

        public Task<ExpenseSummaryResult> GetTotalAsync(ExpenseSummaryRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(Result);
        }
    }
}
