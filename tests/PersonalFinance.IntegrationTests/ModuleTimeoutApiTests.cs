using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PersonalFinance.BudgetModule;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Transactions;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public sealed class ModuleTimeoutApiTests
{
    private readonly Guid user = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 11, 1);

    [Fact]
    public async Task Account_validation_timeout_returns_gateway_timeout_without_creating_operation()
    {
        var validation = new SlowAccountValidation();
        await using var factory = new FinanceApiFactory(services =>
        {
            services.RemoveAll<IAccountValidationService>();
            services.AddSingleton<IAccountValidationService>(validation);
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, user.ToString());
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionService>();
        var account = await accounts.CreateAsync(user, new("Основной", 1000));
        var category = await transactions.CreateCategoryAsync(user, new("Продукты", TransactionType.Expense));
        var request = new CreateTransactionRequest(Guid.NewGuid(), account.Id, category.Id, TransactionType.Expense, 100, From, null);

        using var response = await client.PostAsJsonAsync("/api/v1/transactions", request).WaitAsync(TimeSpan.FromSeconds(10));

        await AssertTimeout(response, AccountErrorCodes.ValidationTimeout,
            "Проверка счёта не завершилась за 2 секунды. Попробуйте ещё раз.");
        Assert.True(validation.Token.IsCancellationRequested);
        Assert.Equal(1, validation.Calls);
        validation.Complete();
        Assert.Empty(await transactions.GetHistoryAsync(user, From, From.AddMonths(1)));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("LIST")]
    public async Task Expense_query_timeout_returns_gateway_timeout_without_changing_budget(string action)
    {
        var query = new SlowExpenseQuery();
        await using var factory = new FinanceApiFactory(services =>
        {
            services.RemoveAll<IExpenseQueryService>();
            services.AddSingleton<IExpenseQueryService>(query);
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, user.ToString());
        using var scope = factory.Services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionService>();
        var category = await transactions.CreateCategoryAsync(user, new("Продукты", TransactionType.Expense));
        var repository = scope.ServiceProvider.GetRequiredService<IBudgetRepository>();
        await repository.SetAsync(new(user, category.Id, From.Year, From.Month, 100));
        var path = $"/api/v1/budgets/{category.Id}/{From.Year}/{From.Month}";
        var request = new SetBudgetRequest(category.Id, From.Year, From.Month, 200);

        using var response = await (action switch
        {
            "PUT" => client.PutAsJsonAsync(path, request),
            "LIST" => client.GetAsync($"/api/v1/budgets?year={From.Year}&month={From.Month}"),
            _ => client.GetAsync(path)
        }).WaitAsync(TimeSpan.FromSeconds(10));

        await AssertTimeout(response, TransactionErrorCodes.ExpenseQueryTimeout,
            "Расчёт расходов не завершился за 2 секунды. Попробуйте ещё раз.");
        Assert.True(query.Token.IsCancellationRequested);
        Assert.Equal(1, query.Calls);
        query.Complete();
        Assert.Equal(100m, (await repository.GetAsync(user, category.Id, From.Year, From.Month))!.Limit);
    }

    private static async Task AssertTimeout(HttpResponseMessage response, string code, string message)
    {
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(504, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, body.RootElement.GetProperty("title").GetString());
    }

    private sealed class SlowAccountValidation : IAccountValidationService
    {
        private readonly TaskCompletionSource<ValidationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }

        public Task<ValidationResult> ValidateAsync(AccountCheckRequest request, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Calls++;
            return completion.Task;
        }

        public void Complete() => completion.TrySetResult(new(true, null));
    }

    private sealed class SlowExpenseQuery : IExpenseQueryService
    {
        private readonly TaskCompletionSource<ExpenseSummaryResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }

        public Task<ExpenseSummaryResult> GetTotalAsync(ExpenseSummaryRequest request, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Calls++;
            return completion.Task;
        }

        public void Complete() => completion.TrySetResult(new(true, 0, null));
    }
}
