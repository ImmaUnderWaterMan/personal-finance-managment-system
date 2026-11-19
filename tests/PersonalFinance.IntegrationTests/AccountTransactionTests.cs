using PersonalFinance.AccountModule;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Transactions;
using PersonalFinance.TransactionModule;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public class AccountTransactionTests
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly AccountService accounts = new(new InMemoryAccountRepository());
    private readonly TransactionService transactions;
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 12, 1);

    public AccountTransactionTests()
    {
        transactions = new(new InMemoryTransactionRepository(), accounts);
    }

    private async Task<CreateTransactionRequest> CreateRequest(Guid userId)
    {
        var account = await accounts.CreateAsync(userId, new("Основной", 1000));
        var category = await transactions.CreateCategoryAsync(userId, new("Продукты", TransactionType.Expense));
        return new(Guid.NewGuid(), account.Id, category.Id, TransactionType.Expense, 250.50m, From, "Покупки");
    }

    [Fact]
    public async Task Registration_uses_real_account_validation_and_preserves_data()
    {
        var request = await CreateRequest(owner);
        var result = await transactions.RegisterAsync(owner, request);
        Assert.Equal(request.Amount, result.Amount);
        Assert.Equal(request.AccountId, result.AccountId);
        Assert.Equal(request.Comment, result.Comment);
        Assert.Equal(result, Assert.Single(await transactions.GetHistoryAsync(owner, From, To)));
        Assert.Equal(new ExpenseSummaryResult(true, request.Amount, null),
            await transactions.GetTotalAsync(new(owner, request.CategoryId, From, To)));
    }

    [Fact]
    public async Task Archived_account_rejects_new_operation()
    {
        var request = await CreateRequest(owner);
        await accounts.ArchiveAsync(owner, request.AccountId);
        var exception = await Assert.ThrowsAsync<TransactionException>(() => transactions.RegisterAsync(owner, request));
        Assert.Equal(AccountErrorCodes.Archived, exception.Code);
        Assert.Empty(await transactions.GetHistoryAsync(owner, From, To));
    }

    [Fact]
    public async Task Foreign_account_rejects_operation()
    {
        var request = await CreateRequest(owner);
        var stranger = Guid.NewGuid();
        var category = await transactions.CreateCategoryAsync(stranger, new("Своя", TransactionType.Expense));
        var exception = await Assert.ThrowsAsync<TransactionException>(() => transactions.RegisterAsync(stranger, request with { CategoryId = category.Id }));
        Assert.Equal(AccountErrorCodes.NotFound, exception.Code);
        Assert.Empty(await transactions.GetHistoryAsync(stranger, From, To));
    }

    [Fact]
    public async Task Foreign_category_rejects_operation()
    {
        var request = await CreateRequest(owner);
        var foreignCategory = await transactions.CreateCategoryAsync(Guid.NewGuid(), new("Чужая", TransactionType.Expense));
        var exception = await Assert.ThrowsAsync<TransactionException>(() => transactions.RegisterAsync(owner, request with { CategoryId = foreignCategory.Id }));
        Assert.Equal(TransactionErrorCodes.CategoryNotFound, exception.Code);
        Assert.Empty(await transactions.GetHistoryAsync(owner, From, To));
    }

    [Fact]
    public async Task Type_mismatch_is_rejected()
    {
        var request = await CreateRequest(owner);
        var exception = await Assert.ThrowsAsync<TransactionException>(() => transactions.RegisterAsync(owner, request with { Type = TransactionType.Income }));
        Assert.Equal(TransactionErrorCodes.InvalidCategoryType, exception.Code);
    }

    [Fact]
    public async Task Replay_returns_existing_operation_even_after_archiving()
    {
        var request = await CreateRequest(owner);
        var original = await transactions.RegisterAsync(owner, request);
        await accounts.ArchiveAsync(owner, request.AccountId);
        Assert.Equal(original, await transactions.RegisterAsync(owner, request));
        Assert.Single(await transactions.GetHistoryAsync(owner, From, To));
    }

    [Fact]
    public async Task Expense_summary_excludes_other_categories_income_and_end_date()
    {
        var request = await CreateRequest(owner);
        await transactions.RegisterAsync(owner, request);
        await transactions.RegisterAsync(owner, request with { RequestId = Guid.NewGuid(), OperationDate = To });
        var other = await transactions.CreateCategoryAsync(owner, new("Транспорт", TransactionType.Expense));
        await transactions.RegisterAsync(owner, request with { RequestId = Guid.NewGuid(), CategoryId = other.Id });
        var income = await transactions.CreateCategoryAsync(owner, new("Зарплата", TransactionType.Income));
        await transactions.RegisterAsync(owner, request with { RequestId = Guid.NewGuid(), CategoryId = income.Id, Type = TransactionType.Income });
        var result = await transactions.GetTotalAsync(new(owner, request.CategoryId, From, To));
        Assert.Equal(request.Amount, result.Total);
        Assert.Equal(3, (await transactions.GetHistoryAsync(owner, From, To)).Count);
    }

    [Fact]
    public async Task Same_request_key_can_be_used_independently_by_different_users()
    {
        var first = await CreateRequest(owner);
        var stranger = Guid.NewGuid();
        var second = (await CreateRequest(stranger)) with { RequestId = first.RequestId };
        var firstResult = await transactions.RegisterAsync(owner, first);
        var secondResult = await transactions.RegisterAsync(stranger, second);
        Assert.NotEqual(firstResult.Id, secondResult.Id);
        Assert.Equal(firstResult, Assert.Single(await transactions.GetHistoryAsync(owner, From, To)));
        Assert.Equal(secondResult, Assert.Single(await transactions.GetHistoryAsync(stranger, From, To)));
        Assert.Equal(TransactionErrorCodes.CategoryNotFound,
            (await transactions.GetTotalAsync(new(stranger, first.CategoryId, From, To))).ErrorCode);
    }
}
