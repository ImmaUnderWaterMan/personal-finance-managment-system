using Microsoft.EntityFrameworkCore;
using PersonalFinance.BudgetModule;
using PersonalFinance.Persistence.Data;

namespace PersonalFinance.Persistence;

public sealed class PostgresBudgetRepository(FinanceDbContext context) : IBudgetRepository
{
    public async Task SetAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.budgets(user_id,category_id,year,month,spending_limit,category_type) VALUES ({budget.UserId},{budget.CategoryId},{budget.Year},{budget.Month},{budget.Limit},1) ON CONFLICT (user_id,category_id,year,month) DO UPDATE SET spending_limit=EXCLUDED.spending_limit", cancellationToken);
    }

    public async Task<Budget?> GetAsync(Guid userId, Guid categoryId, int year, int month, CancellationToken cancellationToken = default)
    {
        var entity = await context.Budgets.AsNoTracking().SingleOrDefaultAsync(b => b.UserId == userId && b.CategoryId == categoryId && b.Year == year && b.Month == month, cancellationToken);
        return entity is null ? null : new(entity.UserId, entity.CategoryId, entity.Year, entity.Month, entity.SpendingLimit);
    }
}
