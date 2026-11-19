namespace PersonalFinance.AccountModule;


public sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly Dictionary<Guid, Account> accounts = [];
    private readonly object syncRoot = new();

    public Task AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            accounts.Add(account.Id, account);
        }

        return Task.CompletedTask;
    }

    public Task<Account?> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            return Task.FromResult(FindOwned(userId, accountId));
        }
    }

    public Task<IReadOnlyList<Account>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            IReadOnlyList<Account> result = accounts.Values
                .Where(account => account.UserId == userId)
                .OrderBy(account => account.Name, StringComparer.Ordinal)
                .ThenBy(account => account.Id)
                .ToArray();

            return Task.FromResult(result);
        }
    }

    public Task<Account?> RenameAsync(Guid userId, Guid accountId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            var account = FindOwned(userId, accountId);
            if (account is null)
            {
                return Task.FromResult<Account?>(null);
            }

            var updated = account with { Name = name };
            accounts[accountId] = updated;
            return Task.FromResult<Account?>(updated);
        }
    }

    public Task<Account?> ArchiveAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            var account = FindOwned(userId, accountId);
            if (account is null)
            {
                return Task.FromResult<Account?>(null);
            }

            var updated = account with { IsArchived = true };
            accounts[accountId] = updated;
            return Task.FromResult<Account?>(updated);
        }
    }

    private Account? FindOwned(Guid userId, Guid accountId) =>
        accounts.TryGetValue(accountId, out var account) && account.UserId == userId
            ? account
            : null;
}
