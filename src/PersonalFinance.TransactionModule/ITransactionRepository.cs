namespace PersonalFinance.TransactionModule;

public interface ITransactionRepository
{
    Task AddCategoryAsync(Category category, CancellationToken cancellationToken = default);
    Task<Category?> GetCategoryAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<FinancialTransaction?> FindByRequestIdAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default);
    Task<FinancialTransaction> AddOrGetAsync(FinancialTransaction transaction, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinancialTransaction>> GetHistoryAsync(Guid userId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken = default);
}
