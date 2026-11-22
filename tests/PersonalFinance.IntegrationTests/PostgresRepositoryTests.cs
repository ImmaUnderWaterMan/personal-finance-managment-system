using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PersonalFinance.AccountModule;
using PersonalFinance.BudgetModule;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Reports;
using PersonalFinance.Contracts.Transactions;
using PersonalFinance.Persistence;
using PersonalFinance.Persistence.Data;
using PersonalFinance.ReportModule;
using PersonalFinance.TransactionModule;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PERSONAL_FINANCE_TEST_DB")))
        {
            Skip = "Задайте PERSONAL_FINANCE_TEST_DB для проверки настоящего PostgreSQL.";
        }
    }
}

public sealed class PostgresRepositoryTests : IAsyncLifetime
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly Guid stranger = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 12, 1);
    private FinanceDbContext context = null!;
    private IDbContextTransaction? transaction;
    private AccountService accounts = null!;
    private TransactionService transactions = null!;
    private BudgetService budgets = null!;
    private ReportService reports = null!;

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("PERSONAL_FINANCE_TEST_DB")
            ?? throw new InvalidOperationException("PERSONAL_FINANCE_TEST_DB не задана.");
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connection).Options;
        context = new FinanceDbContext(options);
        transaction = await context.Database.BeginTransactionAsync();
        foreach (var id in new[] { owner, stranger })
        {
            var email = $"database-test-{id:N}@test.invalid";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO public.users(id,email,display_name,password) VALUES ({id},{email},{"Database test"},{"temporary-test-hash"})");
        }

        accounts = new(new PostgresAccountRepository(context));
        transactions = new(new PostgresTransactionRepository(context), accounts);
        budgets = new(new PostgresBudgetRepository(context), transactions);
        reports = new(accounts, transactions, budgets);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync();
                await transaction.DisposeAsync();
            }
        }
        finally
        {
            if (context is not null)
            {
                await context.DisposeAsync();
            }
        }
    }

    private async Task<CreateTransactionRequest> CreateExpense(decimal amount = 25m)
    {
        var account = await accounts.CreateAsync(owner, new("Основной", 100));
        var category = await transactions.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense));
        return new(Guid.NewGuid(), account.Id, category.Id, TransactionType.Expense, amount, From, null);
    }

    [PostgresFact]
    public async Task Accounts_are_saved_renamed_and_archived()
    {
        var account = await accounts.CreateAsync(owner, new("  Счет  ", -10.25m));
        Assert.Equal(account, await accounts.GetAsync(owner, account.Id));
        Assert.Equal("Новое имя", (await accounts.RenameAsync(owner, account.Id, new("Новое имя"))).Name);
        await accounts.ArchiveAsync(owner, account.Id);
        Assert.Equal(new ValidationResult(false, AccountErrorCodes.Archived),
            await accounts.ValidateAsync(new(owner, account.Id)));
        Assert.True((await accounts.GetAsync(owner, account.Id)).IsArchived);
    }

    [PostgresFact]
    public async Task Foreign_account_is_hidden_and_cannot_be_changed()
    {
        var account = await accounts.CreateAsync(owner, new("Личный", 10));
        Assert.Empty(await accounts.GetAllAsync(stranger));
        var error = await Assert.ThrowsAsync<AccountException>(() => accounts.RenameAsync(stranger, account.Id, new("Чужой")));
        Assert.Equal(AccountErrorCodes.NotFound, error.Code);
        Assert.Equal(account, await accounts.GetAsync(owner, account.Id));
    }

    [PostgresFact]
    public async Task Categories_preserve_type_and_ownership()
    {
        var category = await transactions.CreateCategoryAsync(owner, new("Зарплата", TransactionType.Income));
        Assert.Contains(category, await transactions.GetCategoriesAsync(owner));
        Assert.Empty(await transactions.GetCategoriesAsync(stranger));
        Assert.Equal(TransactionType.Income, category.Type);
    }

    [PostgresFact]
    public async Task Operation_is_read_from_a_separate_context()
    {
        var request = await CreateExpense();
        var saved = await transactions.RegisterAsync(owner, request);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(context.Database.GetDbConnection()).Options;
        await using var reader = new FinanceDbContext(options);
        await reader.Database.UseTransactionAsync(transaction!.GetDbTransaction());
        var persisted = await new PostgresTransactionRepository(reader).FindByRequestIdAsync(owner, request.RequestId);
        Assert.NotNull(persisted);
        Assert.Equal(saved.Id, persisted.Id);
        Assert.Equal(request, persisted.Request);
    }

    [PostgresFact]
    public async Task Replay_does_not_duplicate_operation_and_changed_request_conflicts()
    {
        var request = await CreateExpense();
        var original = await transactions.RegisterAsync(owner, request);
        Assert.Equal(original, await transactions.RegisterAsync(owner, request));
        var error = await Assert.ThrowsAsync<TransactionException>(() => transactions.RegisterAsync(owner, request with { Amount = 99 }));
        Assert.Equal(TransactionErrorCodes.RequestConflict, error.Code);
        Assert.Single(await transactions.GetHistoryAsync(owner, From, To));
    }

    [PostgresFact]
    public async Task Archived_account_rejects_new_operations_and_keeps_saved_history()
    {
        var request = await CreateExpense();
        var saved = await transactions.RegisterAsync(owner, request);
        await accounts.ArchiveAsync(owner, request.AccountId);
        var error = await Assert.ThrowsAsync<TransactionException>(() => transactions.RegisterAsync(owner, request with { RequestId = Guid.NewGuid() }));
        Assert.Equal(AccountErrorCodes.Archived, error.Code);
        Assert.Equal(saved, await transactions.RegisterAsync(owner, request));
        Assert.Single(await transactions.GetHistoryAsync(owner, From, To));
    }

    [PostgresFact]
    public async Task Foreign_category_is_rejected_without_writing_operation()
    {
        var request = await CreateExpense();
        var foreign = await transactions.CreateCategoryAsync(stranger, new("Чужая", TransactionType.Expense));
        var error = await Assert.ThrowsAsync<TransactionException>(() => transactions.RegisterAsync(owner, request with { CategoryId = foreign.Id }));
        Assert.Equal(TransactionErrorCodes.CategoryNotFound, error.Code);
        Assert.Empty(await transactions.GetHistoryAsync(owner, From, To));
    }

    [PostgresFact]
    public async Task Monthly_expenses_exclude_next_month_and_other_users()
    {
        var request = await CreateExpense(12.50m);
        await transactions.RegisterAsync(owner, request);
        await transactions.RegisterAsync(owner, request with { RequestId = Guid.NewGuid(), OperationDate = To });
        Assert.Equal(new ExpenseSummaryResult(true, 12.50m, null),
            await transactions.GetTotalAsync(new(owner, request.CategoryId, From, To)));
        Assert.Empty(await transactions.GetHistoryAsync(stranger, From, To));
    }

    [PostgresFact]
    public async Task Budget_upsert_updates_limit_without_duplicate_rows()
    {
        var request = await CreateExpense(120);
        await transactions.RegisterAsync(owner, request);
        await budgets.SetAsync(owner, new(request.CategoryId, 2026, 11, 100));
        Assert.Equal(BudgetStatus.Exceeded, (await budgets.GetAsync(owner, request.CategoryId, 2026, 11)).Status);
        await budgets.SetAsync(owner, new(request.CategoryId, 2026, 11, 150));
        var summary = await budgets.GetAsync(owner, request.CategoryId, 2026, 11);
        Assert.Equal(30m, summary.Remaining);
        Assert.Equal(BudgetStatus.WithinLimit, summary.Status);
        Assert.Equal(1, await context.Budgets.CountAsync(b => b.UserId == owner));
    }

    [PostgresFact]
    public async Task Report_reads_accounts_operations_and_budgets()
    {
        var request = await CreateExpense(25);
        await transactions.RegisterAsync(owner, request);
        await transactions.RegisterAsync(owner, request with { RequestId = Guid.NewGuid(), Amount = 10, OperationDate = From.AddDays(-1) });
        await budgets.SetAsync(owner, new(request.CategoryId, 2026, 11, 100));
        var report = await reports.GetSummaryAsync(owner, new ReportRequest(From, To));
        var balance = Assert.Single(report.Accounts);
        Assert.Equal(90m, balance.BalanceAtStart);
        Assert.Equal(65m, balance.BalanceAtEnd);
        Assert.Equal(25m, report.TotalExpense);
        Assert.Equal(25m, Assert.Single(await reports.GetMonthlyBudgetsAsync(owner, 2026, 11)).Budget.Spent);
    }

    [PostgresFact]
    public async Task Cancellation_prevents_write()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accounts.CreateAsync(owner, new("Отмененный", 0), cancellation.Token));
        Assert.Empty(await accounts.GetAllAsync(owner));
    }
}
