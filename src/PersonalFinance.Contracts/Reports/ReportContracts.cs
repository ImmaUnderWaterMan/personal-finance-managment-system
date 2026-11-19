using PersonalFinance.Contracts.Budgets;

namespace PersonalFinance.Contracts.Reports;

public record ReportRequest(DateOnly From, DateOnly ToExclusive);

public record CategoryExpenseDto(Guid CategoryId, string Name, decimal Amount);

public record AccountBalanceDto(
    Guid AccountId,
    string Name,
    bool IsArchived,
    decimal BalanceAtStart,
    decimal Income,
    decimal Expense,
    decimal BalanceAtEnd);

public record FinanceReportDto(
    DateOnly From,
    DateOnly ToExclusive,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetIncome,
    IReadOnlyList<CategoryExpenseDto> ExpensesByCategory,
    IReadOnlyList<AccountBalanceDto> Accounts);

public record BudgetReportDto(string CategoryName, BudgetSummaryDto Budget);

public static class ReportErrorCodes
{
    public const string InvalidUser = "INVALID_USER";
    public const string InvalidPeriod = "INVALID_PERIOD";
}

public sealed class ReportException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IReportService
{
    Task<FinanceReportDto> GetSummaryAsync(Guid userId, ReportRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BudgetReportDto>> GetMonthlyBudgetsAsync(Guid userId, int year, int month, CancellationToken cancellationToken = default);
}
