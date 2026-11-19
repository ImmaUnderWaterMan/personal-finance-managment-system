using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Reports;
using PersonalFinance.Contracts.Transactions;

namespace PersonalFinance.ReportModule;

public sealed class ReportService(
    IAccountService accounts,
    ITransactionService transactions,
    IBudgetService budgets) : IReportService
{
    public async Task<FinanceReportDto> GetSummaryAsync(
        Guid userId,
        ReportRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.From >= request.ToExclusive)
        {
            throw new ReportException(ReportErrorCodes.InvalidPeriod, "Начало периода должно быть раньше конца.");
        }

        var userAccounts = await accounts.GetAllAsync(userId, cancellationToken);
        var categories = await transactions.GetCategoriesAsync(userId, cancellationToken);
        var history = await transactions.GetHistoryAsync(userId, DateOnly.MinValue, request.ToExclusive, cancellationToken);
        var period = history.Where(t => t.OperationDate >= request.From).ToArray();
        var income = period.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
        var expense = period.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);
        var byCategory = period.Where(t => t.Type == TransactionType.Expense)
            .GroupBy(t => t.CategoryId)
            .Select(g => new CategoryExpenseDto(g.Key, categoryNames.GetValueOrDefault(g.Key, "Категория недоступна"), g.Sum(t => t.Amount)))
            .OrderByDescending(c => c.Amount)
            .ThenBy(c => c.CategoryId)
            .ToArray();

        var byAccount = history.ToLookup(t => t.AccountId);
        var balances = userAccounts.Select(account =>
        {
            var operations = byAccount[account.Id];
            var start = account.OpeningBalance + operations.Where(t => t.OperationDate < request.From).Sum(SignedAmount);
            var inPeriod = operations.Where(t => t.OperationDate >= request.From).ToArray();
            var accountIncome = inPeriod.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
            var accountExpense = inPeriod.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
            return new AccountBalanceDto(account.Id, account.Name, account.IsArchived,
                start, accountIncome, accountExpense, start + accountIncome - accountExpense);
        }).ToArray();

        return new(request.From, request.ToExclusive, income, expense, income - expense, byCategory, balances);
    }

    public async Task<IReadOnlyList<BudgetReportDto>> GetMonthlyBudgetsAsync(
        Guid userId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        if (year < 1 || year > 9999 || month < 1 || month > 12 || (year == 9999 && month == 12))
        {
            throw new ReportException(ReportErrorCodes.InvalidPeriod, "Укажите корректный месяц с представимым концом периода.");
        }

        var categories = await transactions.GetCategoriesAsync(userId, cancellationToken);
        var result = new List<BudgetReportDto>();
        foreach (var category in categories.Where(c => c.Type == TransactionType.Expense))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var budget = await budgets.GetAsync(userId, category.Id, year, month, cancellationToken);
                result.Add(new(category.Name, budget));
            }
            catch (BudgetException exception) when (exception.Code == BudgetErrorCodes.NotFound)
            {
            }
        }

        return result.OrderBy(r => r.CategoryName, StringComparer.Ordinal).ThenBy(r => r.Budget.CategoryId).ToArray();
    }

    private static decimal SignedAmount(TransactionDto transaction) =>
        transaction.Type == TransactionType.Income ? transaction.Amount : -transaction.Amount;

    private static void EnsureUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ReportException(ReportErrorCodes.InvalidUser, "Пользователь не определен.");
        }
    }
}
