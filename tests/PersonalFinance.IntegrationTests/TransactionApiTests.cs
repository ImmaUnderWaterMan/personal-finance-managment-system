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

public sealed class TransactionApiTests : IAsyncLifetime
{
    private readonly FinanceApiFactory factory = new();
    private readonly Guid owner = Guid.NewGuid();
    private readonly Guid stranger = Guid.NewGuid();
    private readonly DateOnly from = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private HttpClient client = null!;
    private Guid accountId;
    private Guid foreignAccountId;
    private Guid expenseCategoryId;
    private Guid incomeCategoryId;
    private Guid foreignCategoryId;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionService>();
        accountId = (await accounts.CreateAsync(owner, new("Основной", 1000))).Id;
        foreignAccountId = (await accounts.CreateAsync(stranger, new("Чужой", 500))).Id;
        expenseCategoryId = (await transactions.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense))).Id;
        incomeCategoryId = (await transactions.CreateCategoryAsync(owner, new("Зарплата", TransactionType.Income))).Id;
        foreignCategoryId = (await transactions.CreateCategoryAsync(stranger, new("Чужая", TransactionType.Expense))).Id;
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        await factory.DisposeAsync();
    }

    [Theory]
    [InlineData(TransactionType.Income)]
    [InlineData(TransactionType.Expense)]
    public async Task Successful_registration_is_saved_and_updates_report(TransactionType type)
    {
        var request = CreateRequest(type);
        var saved = await Register(request);
        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(new TransactionDto(saved.Id, request.AccountId, request.CategoryId, request.Type,
            request.Amount, request.OperationDate, request.Comment), saved);
        Assert.Equal(saved, Assert.Single(await History()));

        var path = FormattableString.Invariant($"/api/v1/reports/summary?from={from:yyyy-MM-dd}&toExclusive={from.AddMonths(1):yyyy-MM-dd}");
        using var response = await Send(HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        var report = await response.Content.ReadFromJsonAsync<FinanceReportDto>(json);
        var income = type == TransactionType.Income ? request.Amount : 0;
        var expense = type == TransactionType.Expense ? request.Amount : 0;
        Assert.Equal(income, report!.TotalIncome);
        Assert.Equal(expense, report.TotalExpense);
        Assert.Equal(1000 + income - expense, Assert.Single(report.Accounts).BalanceAtEnd);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task Anonymous_requests_are_unauthorized_without_redirect(string method)
    {
        using var message = new HttpRequestMessage(new HttpMethod(method), method == "GET"
            ? HistoryPath(from, from.AddMonths(1))
            : "/api/v1/transactions");
        if (method == "POST") message.Content = JsonContent.Create(CreateRequest(), options: json);
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await History());
    }

    [Fact]
    public async Task Archived_account_returns_conflict_without_creating_operation()
    {
        await ArchiveAccount();
        using var response = await Send(HttpMethod.Post, "/api/v1/transactions", CreateRequest());
        await AssertProblem(response, HttpStatusCode.Conflict, AccountErrorCodes.Archived);
        Assert.Empty(await History());
    }

    [Fact]
    public async Task Foreign_account_returns_not_found_without_creating_operation()
    {
        var request = CreateRequest() with { AccountId = foreignAccountId };
        using var response = await Send(HttpMethod.Post, "/api/v1/transactions", request);
        await AssertProblem(response, HttpStatusCode.NotFound, AccountErrorCodes.NotFound);
        Assert.Empty(await History());
        Assert.Empty(await History(stranger));
    }

    [Fact]
    public async Task Foreign_category_returns_not_found_without_creating_operation()
    {
        var request = CreateRequest() with { CategoryId = foreignCategoryId };
        using var response = await Send(HttpMethod.Post, "/api/v1/transactions", request);
        await AssertProblem(response, HttpStatusCode.NotFound, TransactionErrorCodes.CategoryNotFound);
        Assert.Empty(await History());
        Assert.Empty(await History(stranger));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replay_returns_existing_operation_even_after_archiving(bool archive)
    {
        var request = CreateRequest();
        var original = await Register(request);
        if (archive) await ArchiveAccount();
        var repeated = await Register(request);
        Assert.Equal(original, repeated);
        Assert.Equal(original, Assert.Single(await History()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changed_replay_returns_conflict_and_preserves_original(bool changeAmount)
    {
        var request = CreateRequest();
        var original = await Register(request);
        var changed = changeAmount
            ? request with { Amount = request.Amount + 1 }
            : request with { Comment = "Другое описание" };
        using var response = await Send(HttpMethod.Post, "/api/v1/transactions", changed);
        await AssertProblem(response, HttpStatusCode.Conflict, TransactionErrorCodes.RequestConflict);
        Assert.Equal(original, Assert.Single(await History()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_amount_returns_bad_request(int amount)
    {
        using var response = await Send(HttpMethod.Post, "/api/v1/transactions", CreateRequest() with { Amount = amount });
        await AssertProblem(response, HttpStatusCode.BadRequest, TransactionErrorCodes.InvalidAmount);
        Assert.Empty(await History());
    }

    [Fact]
    public async Task Category_type_mismatch_returns_bad_request()
    {
        var request = CreateRequest() with { Type = TransactionType.Income };
        using var response = await Send(HttpMethod.Post, "/api/v1/transactions", request);
        await AssertProblem(response, HttpStatusCode.BadRequest, TransactionErrorCodes.InvalidCategoryType);
        Assert.Empty(await History());
    }

    [Fact]
    public async Task Empty_request_id_returns_bad_request()
    {
        using var response = await Send(HttpMethod.Post, "/api/v1/transactions", CreateRequest() with { RequestId = Guid.Empty });
        await AssertProblem(response, HttpStatusCode.BadRequest, TransactionErrorCodes.InvalidRequestId);
        Assert.Empty(await History());
    }

    [Fact]
    public async Task History_respects_period_boundaries_and_authenticated_owner()
    {
        var request = CreateRequest();
        var included = await Register(request);
        await Register(request with { RequestId = Guid.NewGuid(), OperationDate = from.AddDays(-1) });
        await Register(request with { RequestId = Guid.NewGuid(), OperationDate = from.AddDays(1) });
        var foreign = await Register(request with
        {
            AccountId = foreignAccountId,
            CategoryId = foreignCategoryId,
            Amount = 99
        }, stranger);

        var path = HistoryPath(from, from.AddDays(1)) + $"&userId={stranger}";
        using var response = await Send(HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        var history = await response.Content.ReadFromJsonAsync<TransactionDto[]>(json);
        Assert.Equal(included, Assert.Single(history!));
        Assert.Equal(foreign, Assert.Single(await History(stranger)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_history_period_returns_bad_request(bool reversed)
    {
        using var response = await Send(HttpMethod.Get, HistoryPath(from, reversed ? from.AddDays(-1) : from));
        await AssertProblem(response, HttpStatusCode.BadRequest, TransactionErrorCodes.InvalidPeriod);
    }

    [Fact]
    public async Task Concurrent_replays_create_only_one_operation()
    {
        var request = CreateRequest();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Register(request)));
        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Equal(results[0], Assert.Single(await History()));
    }

    [Fact]
    public async Task Different_users_can_use_the_same_request_id_independently()
    {
        var request = CreateRequest();
        var own = await Register(request);
        var foreign = await Register(request with
        {
            AccountId = foreignAccountId,
            CategoryId = foreignCategoryId,
            Amount = 99
        }, stranger);
        Assert.NotEqual(own.Id, foreign.Id);
        Assert.Equal(own, Assert.Single(await History()));
        Assert.Equal(foreign, Assert.Single(await History(stranger)));
    }

    private CreateTransactionRequest CreateRequest(TransactionType type = TransactionType.Expense) =>
        new(Guid.NewGuid(), accountId, type == TransactionType.Income ? incomeCategoryId : expenseCategoryId,
            type, 250.50m, from, type == TransactionType.Income ? "Зарплата" : "Покупки");

    private async Task<TransactionDto> Register(CreateTransactionRequest request, Guid? userId = null)
    {
        using var response = await Send(HttpMethod.Post, "/api/v1/transactions", request, userId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return (await response.Content.ReadFromJsonAsync<TransactionDto>(json))!;
    }

    private async Task<TransactionDto[]> History(Guid? userId = null)
    {
        using var response = await Send(HttpMethod.Get, HistoryPath(from, from.AddMonths(1)), userId: userId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionDto[]>(json))!;
    }

    private async Task ArchiveAccount()
    {
        using var response = await Send(HttpMethod.Post, $"/api/v1/accounts/{accountId}/archive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string HistoryPath(DateOnly start, DateOnly toExclusive) =>
        FormattableString.Invariant($"/api/v1/transactions?from={start:yyyy-MM-dd}&toExclusive={toExclusive:yyyy-MM-dd}");

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, Guid? userId = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthenticationHandler.UserHeader, (userId ?? owner).ToString());
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
