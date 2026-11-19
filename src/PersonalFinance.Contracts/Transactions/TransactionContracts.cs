namespace PersonalFinance.Contracts.Transactions;

public enum TransactionType
{
    Income,
    Expense
}

public record CategoryDto(Guid Id, string Name, TransactionType Type);

public record CreateCategoryRequest(string Name, TransactionType Type);

public record CreateTransactionRequest(
    Guid RequestId,
    Guid AccountId,
    Guid CategoryId,
    TransactionType Type,
    decimal Amount,
    DateOnly OperationDate,
    string? Comment);

public record TransactionDto(
    Guid Id,
    Guid AccountId,
    Guid CategoryId,
    TransactionType Type,
    decimal Amount,
    DateOnly OperationDate,
    string? Comment);

public record ExpenseSummaryRequest(Guid UserId, Guid CategoryId, DateOnly From, DateOnly ToExclusive);

public record ExpenseSummaryResult(bool Success, decimal? Total, string? ErrorCode);

public static class TransactionErrorCodes
{
    public const string InvalidUser = "INVALID_USER";
    public const string InvalidName = "INVALID_CATEGORY_NAME";
    public const string CategoryNotFound = "CATEGORY_NOT_FOUND";
    public const string InvalidCategoryType = "INVALID_CATEGORY_TYPE";
    public const string InvalidType = "INVALID_TRANSACTION_TYPE";
    public const string InvalidAmount = "INVALID_AMOUNT";
    public const string InvalidRequestId = "INVALID_REQUEST_ID";
    public const string InvalidComment = "INVALID_COMMENT";
    public const string InvalidPeriod = "INVALID_PERIOD";
    public const string RequestConflict = "REQUEST_CONFLICT";
}

public sealed class TransactionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IExpenseQueryService
{
    Task<ExpenseSummaryResult> GetTotalAsync(ExpenseSummaryRequest request, CancellationToken cancellationToken = default);
}

public interface ITransactionService : IExpenseQueryService
{
    Task<CategoryDto> CreateCategoryAsync(Guid userId, CreateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<TransactionDto> RegisterAsync(Guid userId, CreateTransactionRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransactionDto>> GetHistoryAsync(Guid userId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken = default);
}
