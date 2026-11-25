using PersonalFinance.BudgetModule;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Transactions;
using PersonalFinance.TransactionModule;
using Xunit;

namespace PersonalFinance.UnitTests;

public sealed class ModuleTimeoutTests
{
    private readonly Guid user = Guid.NewGuid();
    private readonly Guid category = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = From.AddMonths(1);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Account_validation_timeout_cancels_call_and_does_not_save_operation(bool observeCancellation)
    {
        var pending = new PendingCall<ValidationResult>(observeCancellation);
        var service = new TransactionService(new InMemoryTransactionRepository(), new AccountValidation(pending));
        var request = await CreateRequest(service);

        var error = await Assert.ThrowsAsync<TransactionException>(() =>
            service.RegisterAsync(user, request).WaitAsync(TestTimeout));

        Assert.Equal(AccountErrorCodes.ValidationTimeout, error.Code);
        Assert.True(pending.Token.IsCancellationRequested);
        Assert.Equal(1, pending.Calls);
        pending.Complete(new(true, null));
        Assert.Empty(await service.GetHistoryAsync(user, From, To));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Account_validation_cancellation_is_not_reported_as_timeout(bool cancelCaller)
    {
        var pending = new PendingCall<ValidationResult>();
        var service = new TransactionService(new InMemoryTransactionRepository(), new AccountValidation(pending));
        var request = await CreateRequest(service);
        using var cancellation = new CancellationTokenSource();
        var operation = service.RegisterAsync(user, request, cancellation.Token);
        await pending.Started.WaitAsync(TestTimeout);

        if (cancelCaller) cancellation.Cancel();
        else pending.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TestTimeout));
        Assert.Equal(cancelCaller, pending.Token.IsCancellationRequested);
        Assert.Equal(1, pending.Calls);
        Assert.Empty(await service.GetHistoryAsync(user, From, To));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expense_query_timeout_cancels_call_and_preserves_existing_limit(bool observeCancellation)
    {
        var repository = new InMemoryBudgetRepository();
        await repository.SetAsync(new(user, category, From.Year, From.Month, 100));
        var pending = new PendingCall<ExpenseSummaryResult>(observeCancellation);
        var service = new BudgetService(repository, new ExpenseQuery(pending));

        var error = await Assert.ThrowsAsync<BudgetException>(() =>
            service.SetAsync(user, new(category, From.Year, From.Month, 200)).WaitAsync(TestTimeout));

        Assert.Equal(TransactionErrorCodes.ExpenseQueryTimeout, error.Code);
        Assert.True(pending.Token.IsCancellationRequested);
        Assert.Equal(1, pending.Calls);
        pending.Complete(new(true, 0, null));
        Assert.Equal(100m, (await repository.GetAsync(user, category, From.Year, From.Month))!.Limit);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expense_query_cancellation_is_not_reported_as_timeout_or_saved(bool cancelCaller)
    {
        var repository = new InMemoryBudgetRepository();
        var pending = new PendingCall<ExpenseSummaryResult>();
        var service = new BudgetService(repository, new ExpenseQuery(pending));
        using var cancellation = new CancellationTokenSource();
        var operation = service.SetAsync(user, new(category, From.Year, From.Month, 100), cancellation.Token);
        await pending.Started.WaitAsync(TestTimeout);

        if (cancelCaller) cancellation.Cancel();
        else pending.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TestTimeout));
        Assert.Equal(cancelCaller, pending.Token.IsCancellationRequested);
        Assert.Equal(1, pending.Calls);
        Assert.Null(await repository.GetAsync(user, category, From.Year, From.Month));
    }

    private async Task<CreateTransactionRequest> CreateRequest(TransactionService service)
    {
        var expenseCategory = await service.CreateCategoryAsync(user, new("Продукты", TransactionType.Expense));
        return new(Guid.NewGuid(), Guid.NewGuid(), expenseCategory.Id, TransactionType.Expense, 100, From, null);
    }

    private sealed class AccountValidation(PendingCall<ValidationResult> pending) : IAccountValidationService
    {
        public Task<ValidationResult> ValidateAsync(AccountCheckRequest request, CancellationToken cancellationToken = default) =>
            pending.Invoke(cancellationToken);
    }

    private sealed class ExpenseQuery(PendingCall<ExpenseSummaryResult> pending) : IExpenseQueryService
    {
        public Task<ExpenseSummaryResult> GetTotalAsync(ExpenseSummaryRequest request, CancellationToken cancellationToken = default) =>
            pending.Invoke(cancellationToken);
    }

    private sealed class PendingCall<T>(bool observeCancellation = true)
    {
        private readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => started.Task;
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }

        public Task<T> Invoke(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Calls++;
            started.TrySetResult();
            return observeCancellation ? completion.Task.WaitAsync(cancellationToken) : completion.Task;
        }

        public void Complete(T result) => completion.TrySetResult(result);
        public void Cancel() => completion.TrySetCanceled();
    }
}
