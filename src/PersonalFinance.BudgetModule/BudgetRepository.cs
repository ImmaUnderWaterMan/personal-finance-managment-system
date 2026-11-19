namespace PersonalFinance.BudgetModule;

public sealed record Budget(Guid UserId, Guid CategoryId, int Year, int Month, decimal Limit);

public interface IBudgetRepository
{
    Task SetAsync(Budget budget, CancellationToken cancellationToken = default);
    Task<Budget?> GetAsync(Guid userId, Guid categoryId, int year, int month, CancellationToken cancellationToken = default);
}

public sealed class InMemoryBudgetRepository : IBudgetRepository
{
    private readonly Dictionary<(Guid UserId, Guid CategoryId, int Year, int Month), Budget> budgets = [];
    private readonly object syncRoot = new();

    public Task SetAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            budgets[(budget.UserId, budget.CategoryId, budget.Year, budget.Month)] = budget;
        }

        return Task.CompletedTask;
    }

    public Task<Budget?> GetAsync(Guid userId, Guid categoryId, int year, int month, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            return Task.FromResult(budgets.GetValueOrDefault((userId, categoryId, year, month)));
        }
    }
}
