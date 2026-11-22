using Microsoft.EntityFrameworkCore;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Transactions;
using PersonalFinance.Persistence.Data;
using PersonalFinance.TransactionModule;
using Category = PersonalFinance.TransactionModule.Category;

namespace PersonalFinance.Persistence;

public sealed class PostgresTransactionRepository(FinanceDbContext context) : ITransactionRepository
{
    public async Task AddCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.categories(id,user_id,name,type) VALUES ({category.Id},{category.UserId},{category.Name},{(int)category.Type})", cancellationToken);
    }

    public async Task<Category?> GetCategoryAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId && c.Id == categoryId, cancellationToken);
        return entity is null ? null : new(entity.Id, entity.UserId, entity.Name, (TransactionType)entity.Type);
    }

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var entities = await context.Categories.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.Name).ToArrayAsync(cancellationToken);
        return entities.Select(c => new Category(c.Id, c.UserId, c.Name, (TransactionType)c.Type)).ToArray();
    }

    public async Task<FinancialTransaction?> FindByRequestIdAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Transactions.AsNoTracking().SingleOrDefaultAsync(t => t.UserId == userId && t.RequestId == requestId, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<FinancialTransaction> AddOrGetAsync(FinancialTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var scope = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var request = transaction.Request;
        var replay = await FindByRequestIdAsync(transaction.UserId, request.RequestId, cancellationToken);
        if (replay is not null) return replay;

        var accounts = await context.Accounts.FromSqlInterpolated($"SELECT * FROM public.accounts WHERE id={request.AccountId} AND user_id={transaction.UserId} FOR SHARE")
            .AsNoTracking().ToListAsync(cancellationToken);
        var account = accounts.SingleOrDefault();
        if (account is null) throw new TransactionException(AccountErrorCodes.NotFound, "Счет не найден.");
        if (account.IsArchived) throw new TransactionException(AccountErrorCodes.Archived, "Счет находится в архиве.");

        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.transactions(id,user_id,request_id,account_id,category_id,type,amount,operation_date,comment) VALUES ({transaction.Id},{transaction.UserId},{request.RequestId},{request.AccountId},{request.CategoryId},{(int)request.Type},{request.Amount},{request.OperationDate},{request.Comment}) ON CONFLICT (user_id,request_id) DO NOTHING", cancellationToken);
        var saved = await FindByRequestIdAsync(transaction.UserId, request.RequestId, cancellationToken)
            ?? throw new InvalidOperationException("Операция не сохранена.");
        if (scope is not null)
        {
            await scope.CommitAsync(cancellationToken);
        }
        return saved;
    }

    public async Task<IReadOnlyList<FinancialTransaction>> GetHistoryAsync(Guid userId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken = default)
    {
        var entities = await context.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.OperationDate >= from && t.OperationDate < toExclusive)
            .OrderByDescending(t => t.OperationDate).ThenBy(t => t.Id).ToArrayAsync(cancellationToken);
        return entities.Select(ToDomain).ToArray();
    }

    private static FinancialTransaction ToDomain(Entities.Transaction entity) => new(entity.Id, entity.UserId,
        new(entity.RequestId, entity.AccountId, entity.CategoryId, (TransactionType)entity.Type, entity.Amount, entity.OperationDate, entity.Comment));
}
