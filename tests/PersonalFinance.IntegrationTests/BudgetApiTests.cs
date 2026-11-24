using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Reports;
using PersonalFinance.Contracts.Transactions;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public sealed class BudgetApiTests : IAsyncLifetime
{
    private readonly FinanceApiFactory factory = new();
    private readonly Guid owner = Guid.NewGuid();
    private readonly Guid stranger = Guid.NewGuid();
    private readonly DateOnly month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private HttpClient client = null!;
    private Guid categoryId;
    private Guid foreignCategoryId;
    private Guid unbudgetedCategoryId;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionService>();
        var budgets = scope.ServiceProvider.GetRequiredService<IBudgetService>();
        var account = await accounts.CreateAsync(owner, new("Основной", 1000));
        categoryId = (await transactions.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense))).Id;
        unbudgetedCategoryId = (await transactions.CreateCategoryAsync(owner, new("Транспорт", TransactionType.Expense))).Id;
        var income = await transactions.CreateCategoryAsync(owner, new("Зарплата", TransactionType.Income));
        var request = new CreateTransactionRequest(Guid.NewGuid(), account.Id, categoryId, TransactionType.Expense, 120, month, null);
        await transactions.RegisterAsync(owner, request);
        await transactions.RegisterAsync(owner, request with { RequestId = Guid.NewGuid(), Amount = 30, OperationDate = month.AddDays(-1) });
        await transactions.RegisterAsync(owner, request with { RequestId = Guid.NewGuid(), CategoryId = income.Id, Type = TransactionType.Income, Amount = 500 });
        await budgets.SetAsync(owner, new(categoryId, month.Year, month.Month, 100));
        var next = month.AddMonths(1);
        await budgets.SetAsync(owner, new(categoryId, next.Year, next.Month, 200));
        foreignCategoryId = (await transactions.CreateCategoryAsync(stranger, new("Продукты", TransactionType.Expense))).Id;
        var foreignAccount = await accounts.CreateAsync(stranger, new("Чужой", 0));
        await transactions.RegisterAsync(stranger, request with { RequestId = Guid.NewGuid(), AccountId = foreignAccount.Id, CategoryId = foreignCategoryId, Amount = 99 });
        await budgets.SetAsync(stranger, new(foreignCategoryId, month.Year, month.Month, 400));
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Get_returns_monthly_summary_and_serializes_status_as_text()
    {
        using var response = await Send(HttpMethod.Get, BudgetPath(categoryId, month));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Exceeded", body.RootElement.GetProperty("status").GetString());
        var budget = body.RootElement.Deserialize<BudgetSummaryDto>(json);
        Assert.Equal(new(categoryId, month.Year, month.Month, 100, 120, -20, BudgetStatus.Exceeded), budget);
    }

    [Fact]
    public async Task Anonymous_request_returns_unauthorized_without_redirect()
    {
        using var response = await client.GetAsync(BudgetPath(categoryId, month));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_budget_returns_not_found(bool categoryExists)
    {
        using var response = await Send(HttpMethod.Get, BudgetPath(categoryExists ? unbudgetedCategoryId : Guid.NewGuid(), month));
        await AssertProblem(response, HttpStatusCode.NotFound, BudgetErrorCodes.NotFound);
    }

    [Fact]
    public async Task Foreign_budget_is_hidden()
    {
        using var response = await Send(HttpMethod.Get, BudgetPath(foreignCategoryId, month));
        await AssertProblem(response, HttpStatusCode.NotFound, BudgetErrorCodes.NotFound);
    }

    [Fact]
    public async Task Different_months_have_independent_limits_and_spending()
    {
        var next = month.AddMonths(1);
        using var response = await Send(HttpMethod.Get, BudgetPath(categoryId, next));
        response.EnsureSuccessStatusCode();
        var budget = await response.Content.ReadFromJsonAsync<BudgetSummaryDto>(json);
        Assert.Equal(new(categoryId, next.Year, next.Month, 200, 0, 200, BudgetStatus.WithinLimit), budget);
    }

    [Fact]
    public async Task Query_user_id_cannot_replace_the_authenticated_owner()
    {
        using var response = await Send(HttpMethod.Get, BudgetPath(categoryId, month) + $"?userId={stranger}");
        response.EnsureSuccessStatusCode();
        var budget = await response.Content.ReadFromJsonAsync<BudgetSummaryDto>(json);
        Assert.Equal(100, budget!.Limit);
        Assert.Equal(120, budget.Spent);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10000, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(9999, 12)]
    public async Task Invalid_period_returns_bad_request(int year, int selectedMonth)
    {
        var path = FormattableString.Invariant($"/api/v1/budgets/{categoryId}/{year}/{selectedMonth}");
        using var response = await Send(HttpMethod.Get, path);
        await AssertProblem(response, HttpStatusCode.BadRequest, BudgetErrorCodes.InvalidPeriod);
    }

    [Fact]
    public async Task Updated_limit_is_returned_by_get()
    {
        var path = BudgetPath(categoryId, month);
        using var update = await Send(HttpMethod.Put, path, new SetBudgetRequest(categoryId, month.Year, month.Month, 300));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var response = await Send(HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        var budget = await response.Content.ReadFromJsonAsync<BudgetSummaryDto>(json);
        Assert.Equal(new(categoryId, month.Year, month.Month, 300, 120, 180, BudgetStatus.WithinLimit), budget);
    }

    [Fact]
    public async Task Single_budget_matches_the_monthly_list()
    {
        using var single = await Send(HttpMethod.Get, BudgetPath(categoryId, month));
        single.EnsureSuccessStatusCode();
        var budget = await single.Content.ReadFromJsonAsync<BudgetSummaryDto>(json);
        var path = FormattableString.Invariant($"/api/v1/budgets?year={month.Year}&month={month.Month}");
        using var list = await Send(HttpMethod.Get, path);
        list.EnsureSuccessStatusCode();
        var items = await list.Content.ReadFromJsonAsync<BudgetReportDto[]>(json);
        Assert.Equal(budget, Assert.Single(items!).Budget);
    }

    private static string BudgetPath(Guid id, DateOnly selectedMonth) =>
        FormattableString.Invariant($"/api/v1/budgets/{id}/{selectedMonth.Year}/{selectedMonth.Month}");

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthenticationHandler.UserHeader, owner.ToString());
        if (body is not null) request.Content = JsonContent.Create(body, options: json);
        return await client.SendAsync(request);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }
}
