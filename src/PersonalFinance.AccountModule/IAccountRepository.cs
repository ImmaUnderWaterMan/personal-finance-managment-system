namespace PersonalFinance.AccountModule;

public interface IAccountRepository
{
    Task AddAsync(Account account, CancellationToken cancellationToken = default);
    Task<Account?> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Account?> RenameAsync(Guid userId, Guid accountId, string name, CancellationToken cancellationToken = default);
    Task<Account?> ArchiveAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default);
}
