using Microsoft.EntityFrameworkCore;
using PersonalFinance.AccountModule;
using PersonalFinance.Persistence.Data;
using AccountEntity = PersonalFinance.Persistence.Entities.Account;

namespace PersonalFinance.Persistence;

public sealed class PostgresAccountRepository(FinanceDbContext context) : IAccountRepository
{
    public async Task AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        var entity = new AccountEntity
        {
            Id = account.Id,
            UserId = account.UserId,
            Name = account.Name,
            OpeningBalance = account.OpeningBalance,
            IsArchived = account.IsArchived
        };
        context.Accounts.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            context.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<Account?> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.UserId == userId && a.Id == accountId, cancellationToken);
        return entity is null ? null : ToAccount(entity);
    }

    public async Task<IReadOnlyList<Account>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var entities = await context.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Name)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        return entities.Select(ToAccount).ToArray();
    }

    public async Task<Account?> RenameAsync(Guid userId, Guid accountId, string name, CancellationToken cancellationToken = default)
    {
        var changed = await context.Accounts.Where(a => a.UserId == userId && a.Id == accountId)
            .ExecuteUpdateAsync(update => update.SetProperty(a => a.Name, name), cancellationToken);
        return changed == 0 ? null : await GetAsync(userId, accountId, cancellationToken);
    }

    public async Task<Account?> ArchiveAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var changed = await context.Accounts.Where(a => a.UserId == userId && a.Id == accountId)
            .ExecuteUpdateAsync(update => update.SetProperty(a => a.IsArchived, true), cancellationToken);
        return changed == 0 ? null : await GetAsync(userId, accountId, cancellationToken);
    }

    private static Account ToAccount(AccountEntity entity) =>
        new(entity.Id, entity.UserId, entity.Name, entity.OpeningBalance, entity.IsArchived);
}
