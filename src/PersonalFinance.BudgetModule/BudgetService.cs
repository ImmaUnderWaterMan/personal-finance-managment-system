using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Transactions;

namespace PersonalFinance.BudgetModule;

public sealed class BudgetService(IBudgetRepository repository, IExpenseQueryService expenses) : IBudgetService
{
    public async Task<BudgetDto> SetAsync(Guid userId, SetBudgetRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureUser(userId);
        var (from, to) = GetPeriod(request.Year, request.Month);
        if (request.Limit <= 0 || request.Limit > 9999999999999999.99m || decimal.Round(request.Limit, 2) != request.Limit)
        {
            throw new BudgetException(BudgetErrorCodes.InvalidLimit, "Лимит должен быть положительным и соответствовать numeric(18,2).");
        }

        await QueryExpenses(userId, request.CategoryId, from, to, cancellationToken);
        await repository.SetAsync(new(userId, request.CategoryId, request.Year, request.Month, request.Limit), cancellationToken);
        return new(request.CategoryId, request.Year, request.Month, request.Limit);
    }

    public async Task<BudgetSummaryDto> GetAsync(Guid userId, Guid categoryId, int year, int month, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        var (from, to) = GetPeriod(year, month);
        var budget = await repository.GetAsync(userId, categoryId, year, month, cancellationToken)
            ?? throw new BudgetException(BudgetErrorCodes.NotFound, "Бюджет не найден.");
        var spent = await QueryExpenses(userId, categoryId, from, to, cancellationToken);
        return new(categoryId, year, month, budget.Limit, spent, budget.Limit - spent,
            spent > budget.Limit ? BudgetStatus.Exceeded : BudgetStatus.WithinLimit);
    }

    private async Task<decimal> QueryExpenses(Guid userId, Guid categoryId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ExpenseSummaryResult result;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                result = await expenses.GetTotalAsync(new(userId, categoryId, from, to), timeout.Token)
                    .WaitAsync(timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new BudgetException(TransactionErrorCodes.ExpenseQueryTimeout,
                    "Расчёт расходов не завершился за 2 секунды. Попробуйте ещё раз.");
            }
        }

        if (!result.Success || result.Total is null || result.Total < 0)
        {
            throw new BudgetException(result.ErrorCode ?? BudgetErrorCodes.ExpenseQueryFailed, "Не удалось получить расходы по категории.");
        }

        return result.Total.Value;
    }

    private static void EnsureUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new BudgetException(BudgetErrorCodes.InvalidUser, "Пользователь не определен.");
        }
    }

    private static (DateOnly From, DateOnly To) GetPeriod(int year, int month)
    {
        if (year < 1 || year > 9999 || month < 1 || month > 12 || (year == 9999 && month == 12))
        {
            throw new BudgetException(BudgetErrorCodes.InvalidPeriod, "Укажите месяц с представимой датой начала следующего месяца.");
        }

        var from = new DateOnly(year, month, 1);
        return (from, from.AddMonths(1));
    }
}
