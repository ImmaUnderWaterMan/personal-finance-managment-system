namespace PersonalFinance.Contracts.Accounts;

public record AccountDto(Guid Id, string Name, decimal OpeningBalance, bool IsArchived);

public record CreateAccountRequest(string Name, decimal OpeningBalance);

public record RenameAccountRequest(string Name);

public record AccountCheckRequest(Guid UserId, Guid AccountId);

public record ValidationResult(bool IsValid, string? ErrorCode);

public static class AccountErrorCodes
{
    public const string NotFound = "ACCOUNT_NOT_FOUND";
    public const string ValidationTimeout = "ACCOUNT_VALIDATION_TIMEOUT";
    public const string Archived = "ACCOUNT_ARCHIVED";
    public const string InvalidName = "INVALID_ACCOUNT_NAME";
    public const string InvalidOpeningBalance = "INVALID_OPENING_BALANCE";
    public const string InvalidUser = "INVALID_USER";
}

public class AccountException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IAccountValidationService
{
    Task<ValidationResult> ValidateAsync(
        AccountCheckRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAccountService : IAccountValidationService
{
    Task<AccountDto> CreateAsync(Guid userId, CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AccountDto> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountDto> RenameAsync(Guid userId, Guid accountId, RenameAccountRequest request, CancellationToken cancellationToken = default);
    Task<AccountDto> ArchiveAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default);
}
