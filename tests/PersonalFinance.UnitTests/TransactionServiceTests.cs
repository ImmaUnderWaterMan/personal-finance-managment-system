using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Transactions;
using PersonalFinance.TransactionModule;
using Xunit;

namespace PersonalFinance.UnitTests;

public class TransactionServiceTests
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly Guid account = Guid.NewGuid();
    private readonly TransactionService service = new(new InMemoryTransactionRepository(), new AvailableAccount());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public async Task Invalid_amount_does_not_create_operation(double amount)
    {
        var request = new CreateTransactionRequest(Guid.NewGuid(), account, Guid.NewGuid(), TransactionType.Expense,
            (decimal)amount, new(2026, 11, 1), null);
        var exception = await Assert.ThrowsAsync<TransactionException>(() => service.RegisterAsync(owner, request));
        Assert.Equal(TransactionErrorCodes.InvalidAmount, exception.Code);
        Assert.Empty(await service.GetHistoryAsync(owner, new(2026, 11, 1), new(2026, 12, 1)));
    }

    [Fact]
    public async Task Category_name_is_trimmed_and_owned()
    {
        var category = await service.CreateCategoryAsync(owner, new("  Продукты  ", TransactionType.Expense));
        Assert.Equal("Продукты", category.Name);
        Assert.Contains(category, await service.GetCategoriesAsync(owner));
        Assert.Empty(await service.GetCategoriesAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task Invalid_category_name_is_rejected(string? name)
    {
        var exception = await Assert.ThrowsAsync<TransactionException>(() => service.CreateCategoryAsync(owner, new(name!, TransactionType.Expense)));
        Assert.Equal(TransactionErrorCodes.InvalidName, exception.Code);
    }

    [Fact]
    public async Task Undefined_type_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<TransactionException>(() => service.CreateCategoryAsync(owner, new("Продукты", (TransactionType)100)));
        Assert.Equal(TransactionErrorCodes.InvalidType, exception.Code);
    }

    [Fact]
    public async Task Invalid_period_returns_error_instead_of_zero()
    {
        var result = await service.GetTotalAsync(new(owner, Guid.NewGuid(), new(2026, 12, 1), new(2026, 11, 1)));
        Assert.False(result.Success);
        Assert.Null(result.Total);
        Assert.Equal(TransactionErrorCodes.InvalidPeriod, result.ErrorCode);
    }

    [Fact]
    public async Task Expense_summary_rejects_income_category()
    {
        var category = await service.CreateCategoryAsync(owner, new("Зарплата", TransactionType.Income));
        var result = await service.GetTotalAsync(new(owner, category.Id, new(2026, 11, 1), new(2026, 12, 1)));
        Assert.Equal(new ExpenseSummaryResult(false, null, TransactionErrorCodes.InvalidCategoryType), result);
    }

    [Fact]
    public async Task No_expenses_returns_successful_zero()
    {
        var category = await service.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense));
        Assert.Equal(new ExpenseSummaryResult(true, 0, null),
            await service.GetTotalAsync(new(owner, category.Id, new(2026, 11, 1), new(2026, 12, 1))));
    }

    [Fact]
    public async Task Cancellation_does_not_add_category()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense), cancellation.Token));
        Assert.Empty(await service.GetCategoriesAsync(owner));
    }

    [Fact]
    public async Task Empty_user_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<TransactionException>(() => service.GetCategoriesAsync(Guid.Empty));
        Assert.Equal(TransactionErrorCodes.InvalidUser, exception.Code);
    }

    [Fact]
    public async Task Changed_request_is_a_conflict()
    {
        var category = await service.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense));
        var request = new CreateTransactionRequest(Guid.NewGuid(), account, category.Id, TransactionType.Expense, 100, new(2026, 11, 1), null);
        await service.RegisterAsync(owner, request);
        var exception = await Assert.ThrowsAsync<TransactionException>(() => service.RegisterAsync(owner, request with { Amount = 200 }));
        Assert.Equal(TransactionErrorCodes.RequestConflict, exception.Code);
        Assert.Single(await service.GetHistoryAsync(owner, new(2026, 11, 1), new(2026, 12, 1)));
    }

    [Fact]
    public async Task Concurrent_requests_with_same_key_create_one_operation()
    {
        var repository = new InMemoryTransactionRepository();
        var gate = new GatedAccount();
        var concurrentService = new TransactionService(repository, gate);
        var category = await concurrentService.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense));
        var request = new CreateTransactionRequest(Guid.NewGuid(), account, category.Id, TransactionType.Expense, 100, new(2026, 11, 1), null);
        var first = concurrentService.RegisterAsync(owner, request);
        var second = concurrentService.RegisterAsync(owner, request);
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(results[0], results[1]);
        Assert.Single(await concurrentService.GetHistoryAsync(owner, new(2026, 11, 1), new(2026, 12, 1)));
    }

    private sealed class AvailableAccount : IAccountValidationService
    {
        public Task<ValidationResult> ValidateAsync(AccountCheckRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ValidationResult(true, null));
    }

    private sealed class GatedAccount : IAccountValidationService
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public async Task<ValidationResult> ValidateAsync(AccountCheckRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                gate.TrySetResult();
            }

            await gate.Task.WaitAsync(cancellationToken);
            return new(true, null);
        }
    }
}
