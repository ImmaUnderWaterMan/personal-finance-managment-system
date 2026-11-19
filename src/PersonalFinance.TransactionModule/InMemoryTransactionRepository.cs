namespace PersonalFinance.TransactionModule;

public sealed class InMemoryTransactionRepository : ITransactionRepository
{
    private readonly Dictionary<Guid, Category> categories = [];
    private readonly Dictionary<(Guid UserId, Guid RequestId), FinancialTransaction> transactions = [];
    private readonly object syncRoot = new();

    public Task AddCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            categories.Add(category.Id, category);
        }

        return Task.CompletedTask;
    }

    public Task<Category?> GetCategoryAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            return Task.FromResult(categories.TryGetValue(categoryId, out var category) && category.UserId == userId
                ? category
                : null);
        }
    }

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            return Task.FromResult<IReadOnlyList<Category>>(categories.Values
                .Where(category => category.UserId == userId)
                .OrderBy(category => category.Name, StringComparer.Ordinal)
                .ToArray());
        }
    }

    public Task<FinancialTransaction?> FindByRequestIdAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            return Task.FromResult(transactions.GetValueOrDefault((userId, requestId)));
        }
    }

    public Task<FinancialTransaction> AddOrGetAsync(FinancialTransaction transaction, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            var key = (transaction.UserId, transaction.Request.RequestId);
            if (transactions.TryGetValue(key, out var existing))
            {
                return Task.FromResult(existing);
            }

            transactions.Add(key, transaction);
            return Task.FromResult(transaction);
        }
    }

    public Task<IReadOnlyList<FinancialTransaction>> GetHistoryAsync(
        Guid userId,
        DateOnly from,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            return Task.FromResult<IReadOnlyList<FinancialTransaction>>(transactions.Values
                .Where(transaction => transaction.UserId == userId &&
                    transaction.Request.OperationDate >= from && transaction.Request.OperationDate < toExclusive)
                .OrderByDescending(transaction => transaction.Request.OperationDate)
                .ThenBy(transaction => transaction.Id)
                .ToArray());
        }
    }
}
