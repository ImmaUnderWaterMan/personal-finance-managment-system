namespace PersonalFinance.Contracts.Budgets;

public enum BudgetStatus { WithinLimit, Exceeded }

public record SetBudgetRequest(Guid CategoryId, int Year, int Month, decimal Limit);
public record BudgetDto(Guid CategoryId, int Year, int Month, decimal Limit);
public record BudgetSummaryDto(Guid CategoryId, int Year, int Month, decimal Limit, decimal Spent, decimal Remaining, BudgetStatus Status);

public sealed class BudgetException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class BudgetErrorCodes
{
    public const string NotFound = "BUDGET_NOT_FOUND";
    public const string InvalidUser = "INVALID_USER";
    public const string InvalidPeriod = "INVALID_BUDGET_PERIOD";
    public const string InvalidLimit = "INVALID_BUDGET_LIMIT";
    public const string ExpenseQueryFailed = "EXPENSE_QUERY_FAILED";
}

public interface IBudgetService
{
    Task<BudgetDto> SetAsync(Guid userId, SetBudgetRequest request, CancellationToken cancellationToken = default);
    Task<BudgetSummaryDto> GetAsync(Guid userId, Guid categoryId, int year, int month, CancellationToken cancellationToken = default);
}
