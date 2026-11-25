using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Reports;
using PersonalFinance.Contracts.Transactions;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public sealed class ReportApiTests : IAsyncLifetime
{
    private readonly FinanceApiFactory factory = new();
    private readonly Guid owner = Guid.NewGuid();
    private readonly Guid stranger = Guid.NewGuid();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = From.AddMonths(1);
    private HttpClient client = null!;
    private IServiceScope scope = null!;
    private IAccountService accounts = null!;
    private ITransactionService transactions = null!;

    public Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        scope = factory.Services.CreateScope();
        accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        transactions = scope.ServiceProvider.GetRequiredService<ITransactionService>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        scope?.Dispose();
        client?.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Summary_returns_period_totals_and_balances_for_each_account()
    {
        var main = await accounts.CreateAsync(owner, new("Основной", 1000));
        var savings = await accounts.CreateAsync(owner, new("Накопления", 200));
        var food = await transactions.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense));
        var salary = await transactions.CreateCategoryAsync(owner, new("Зарплата", TransactionType.Income));
        await Add(main.Id, salary.Id, TransactionType.Income, 500, From.AddDays(-1));
        await Add(main.Id, food.Id, TransactionType.Expense, 50, From.AddDays(-1));
        await Add(main.Id, food.Id, TransactionType.Expense, 100, From);
        await Add(main.Id, salary.Id, TransactionType.Income, 200, To.AddDays(-1));
        await Add(savings.Id, food.Id, TransactionType.Expense, 25, From.AddDays(1));
        await Add(main.Id, food.Id, TransactionType.Expense, 900, To);

        var report = await Summary();

        Assert.Equal(From, report.From);
        Assert.Equal(To, report.ToExclusive);
        Assert.Equal(200m, report.TotalIncome);
        Assert.Equal(125m, report.TotalExpense);
        Assert.Equal(75m, report.NetIncome);
        Assert.Equal(2, report.Accounts.Count);
        Assert.Equal(new AccountBalanceDto(main.Id, main.Name, false, 1450, 200, 100, 1550),
            report.Accounts.Single(account => account.AccountId == main.Id));
        Assert.Equal(new AccountBalanceDto(savings.Id, savings.Name, false, 200, 0, 25, 175),
            report.Accounts.Single(account => account.AccountId == savings.Id));
        Assert.Equal(new CategoryExpenseDto(food.Id, food.Name, 125), Assert.Single(report.ExpensesByCategory));
    }

    [Fact]
    public async Task Grouping_uses_category_ids_and_preserves_decimal_precision()
    {
        var first = await accounts.CreateAsync(owner, new("Счёт", 0));
        var second = await accounts.CreateAsync(owner, new("Счёт", 0));
        var food = await transactions.CreateCategoryAsync(owner, new("Еда", TransactionType.Expense));
        var otherFood = await transactions.CreateCategoryAsync(owner, new("Еда", TransactionType.Expense));
        var income = await transactions.CreateCategoryAsync(owner, new("Доход", TransactionType.Income));
        await Add(first.Id, food.Id, TransactionType.Expense, 10.10m, From);
        await Add(second.Id, food.Id, TransactionType.Expense, 0.20m, From);
        await Add(first.Id, otherFood.Id, TransactionType.Expense, 0.02m, From);
        await Add(first.Id, income.Id, TransactionType.Income, 0.01m, From);

        var report = await Summary();

        Assert.Equal(10.32m, report.TotalExpense);
        Assert.Equal(0.01m, report.TotalIncome);
        Assert.Equal(-10.31m, report.NetIncome);
        Assert.Equal(new[] { new CategoryExpenseDto(food.Id, "Еда", 10.30m), new(otherFood.Id, "Еда", 0.02m) },
            report.ExpensesByCategory);
        Assert.Equal(2, report.Accounts.Count);
        Assert.Equal(-10.11m, report.Accounts.Single(account => account.AccountId == first.Id).BalanceAtEnd);
        Assert.Equal(-0.20m, report.Accounts.Single(account => account.AccountId == second.Id).BalanceAtEnd);
    }

    [Fact]
    public async Task Archived_account_remains_in_report_with_its_operations()
    {
        var account = await accounts.CreateAsync(owner, new("Архивный", 0));
        var category = await transactions.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense));
        await Add(account.Id, category.Id, TransactionType.Expense, 100, From);
        await accounts.ArchiveAsync(owner, account.Id);

        var report = await Summary();

        Assert.Equal(new AccountBalanceDto(account.Id, account.Name, true, 0, 0, 100, -100), Assert.Single(report.Accounts));
        Assert.Equal(100m, report.TotalExpense);
    }

    [Fact]
    public async Task Opening_balance_is_preserved_without_becoming_income()
    {
        var account = await accounts.CreateAsync(owner, new("Накопления", 500));

        var report = await Summary();

        Assert.Equal(new AccountBalanceDto(account.Id, account.Name, false, 500, 0, 0, 500), Assert.Single(report.Accounts));
        Assert.Equal(0m, report.TotalIncome);
        Assert.Equal(0m, report.TotalExpense);
        Assert.Equal(0m, report.NetIncome);
        Assert.Empty(report.ExpensesByCategory);
    }

    [Fact]
    public async Task User_without_accounts_receives_empty_report()
    {
        var report = await Summary();

        Assert.Empty(report.Accounts);
        Assert.Empty(report.ExpensesByCategory);
        Assert.Equal(0m, report.TotalIncome);
        Assert.Equal(0m, report.TotalExpense);
        Assert.Equal(0m, report.NetIncome);
    }

    [Fact]
    public async Task Foreign_data_and_query_user_id_cannot_change_report_owner()
    {
        var own = await accounts.CreateAsync(owner, new("Свой", 10));
        var foreign = await accounts.CreateAsync(stranger, new("Чужой", 1000));
        var category = await transactions.CreateCategoryAsync(stranger, new("Чужая", TransactionType.Expense));
        await Add(foreign.Id, category.Id, TransactionType.Expense, 100, From, stranger);
        using var response = await Send(ReportPath(From, To) + $"&userId={stranger}");
        var report = await ReadReport(response);

        Assert.Equal(own.Id, Assert.Single(report.Accounts).AccountId);
        Assert.Empty(report.ExpensesByCategory);
        Assert.Equal(0m, report.TotalExpense);
        using var foreignResponse = await Send(ReportPath(From, To), stranger);
        var foreignReport = await ReadReport(foreignResponse);
        Assert.Equal(foreign.Id, Assert.Single(foreignReport.Accounts).AccountId);
        Assert.Equal(100m, foreignReport.TotalExpense);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(30, false)]
    public async Task Period_includes_start_and_excludes_end(int dayOffset, bool included)
    {
        var account = await accounts.CreateAsync(owner, new("Основной", 100));
        var category = await transactions.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense));
        await Add(account.Id, category.Id, TransactionType.Expense, 25, From.AddDays(dayOffset));

        var report = await Summary();

        Assert.Equal(included ? 25m : 0m, report.TotalExpense);
        var balance = Assert.Single(report.Accounts);
        Assert.Equal(dayOffset < 0 ? 75m : 100m, balance.BalanceAtStart);
        Assert.Equal(dayOffset < 30 ? 75m : 100m, balance.BalanceAtEnd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_period_returns_bad_request(bool reversed)
    {
        using var response = await Send(ReportPath(From, reversed ? From.AddDays(-1) : From));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ReportErrorCodes.InvalidPeriod, body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("from=invalid&toExclusive=2026-12-01")]
    [InlineData("from=2026-11-01&toExclusive=2026-02-30")]
    public async Task Malformed_date_returns_validation_problem(string query)
    {
        using var response = await Send("/api/v1/reports/summary?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEmpty(body.RootElement.GetProperty("errors").EnumerateObject());
    }

    [Fact]
    public async Task Anonymous_request_returns_unauthorized_without_redirect()
    {
        using var response = await client.GetAsync(ReportPath(From, To));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private Task<TransactionDto> Add(Guid accountId, Guid categoryId, TransactionType type, decimal amount,
        DateOnly date, Guid? userId = null) =>
        transactions.RegisterAsync(userId ?? owner, new(Guid.NewGuid(), accountId, categoryId, type, amount, date, null));

    private async Task<FinanceReportDto> Summary()
    {
        using var response = await Send(ReportPath(From, To));
        return await ReadReport(response);
    }

    private static string ReportPath(DateOnly from, DateOnly to) =>
        FormattableString.Invariant($"/api/v1/reports/summary?from={from:yyyy-MM-dd}&toExclusive={to:yyyy-MM-dd}");

    private async Task<HttpResponseMessage> Send(string path, Guid? userId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestAuthenticationHandler.UserHeader, (userId ?? owner).ToString());
        return await client.SendAsync(request);
    }

    private async Task<FinanceReportDto> ReadReport(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var report = await response.Content.ReadFromJsonAsync<FinanceReportDto>(json);
        Assert.NotNull(report);
        return report;
    }
}
