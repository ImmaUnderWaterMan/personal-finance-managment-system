using PersonalFinance.Contracts.Accounts;

namespace PersonalFinance.AccountModule;

public sealed class AccountService(IAccountRepository repository) : IAccountService
{
    public async Task<AccountDto> CreateAsync(
        Guid userId,
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        ArgumentNullException.ThrowIfNull(request);
        var name = ValidateName(request.Name);

        if (request.OpeningBalance < -9999999999999999.99m ||
            request.OpeningBalance > 9999999999999999.99m ||
            decimal.Round(request.OpeningBalance, 2) != request.OpeningBalance)
        {
            throw new AccountException(AccountErrorCodes.InvalidOpeningBalance,
                "Начальный остаток должен содержать не более двух знаков после запятой и соответствовать numeric(18,2).");
        }

        var account = new Account(Guid.NewGuid(), userId, name, request.OpeningBalance, false);
        await repository.AddAsync(account, cancellationToken);
        return ToDto(account);
    }

    public async Task<IReadOnlyList<AccountDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        var accounts = await repository.GetAllAsync(userId, cancellationToken);
        return accounts.Select(ToDto).ToArray();
    }

    public async Task<AccountDto> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        var account = await repository.GetAsync(userId, accountId, cancellationToken);
        return ToDto(account ?? throw NotFound());
    }

    public async Task<AccountDto> RenameAsync(
        Guid userId,
        Guid accountId,
        RenameAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        ArgumentNullException.ThrowIfNull(request);
        var name = ValidateName(request.Name);
        var account = await repository.RenameAsync(userId, accountId, name, cancellationToken);
        return ToDto(account ?? throw NotFound());
    }

    public async Task<AccountDto> ArchiveAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        var account = await repository.ArchiveAsync(userId, accountId, cancellationToken);
        return ToDto(account ?? throw NotFound());
    }

    public async Task<ValidationResult> ValidateAsync(AccountCheckRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureUser(request.UserId);
        var account = await repository.GetAsync(request.UserId, request.AccountId, cancellationToken);

        if (account is null)
        {
            return new ValidationResult(false, AccountErrorCodes.NotFound);
        }

        return account.IsArchived
            ? new ValidationResult(false, AccountErrorCodes.Archived)
            : new ValidationResult(true, null);
    }

    private static void EnsureUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new AccountException(AccountErrorCodes.InvalidUser, "Пользователь не определен.");
        }
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 100)
        {
            throw new AccountException(AccountErrorCodes.InvalidName, "Название счета должно содержать от 1 до 100 символов.");
        }

        return trimmed;
    }

    private static AccountException NotFound() => new(AccountErrorCodes.NotFound, "Счет не найден.");

    private static AccountDto ToDto(Account account) =>
        new(account.Id, account.Name, account.OpeningBalance, account.IsArchived);
}
