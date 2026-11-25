using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Transactions;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public sealed class AccountApiTests : IAsyncLifetime
{
    private readonly FinanceApiFactory factory = new();
    private readonly Guid owner = Guid.NewGuid();
    private readonly Guid stranger = Guid.NewGuid();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private HttpClient client = null!;
    private AccountDto ownAccount = null!;
    private AccountDto foreignAccount = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        ownAccount = await accounts.CreateAsync(owner, new("Основной", 1000));
        foreignAccount = await accounts.CreateAsync(stranger, new("Чужой", 500));
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        await factory.DisposeAsync();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-25.50")]
    [InlineData("9999999999999999.99")]
    [InlineData("-9999999999999999.99")]
    public async Task Creation_trims_name_and_preserves_exact_opening_balance(string balance)
    {
        var amount = decimal.Parse(balance, CultureInfo.InvariantCulture);
        using var response = await Send(HttpMethod.Post, "/api/v1/accounts", new CreateAccountRequest("  Наличные  ", amount));
        var saved = await ReadSuccess<AccountDto>(response);

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(new AccountDto(saved.Id, "Наличные", amount, false), saved);
        var accounts = await Accounts();
        Assert.Equal(2, accounts.Length);
        Assert.Contains(saved, accounts);
        Assert.Contains(ownAccount, accounts);
        Assert.Equal(foreignAccount, Assert.Single(await Accounts(stranger)));
    }

    [Theory]
    [InlineData("1.001")]
    [InlineData("10000000000000000")]
    [InlineData("-10000000000000000")]
    public async Task Invalid_opening_balance_returns_bad_request_without_creating_account(string balance)
    {
        var request = new CreateAccountRequest("Наличные", decimal.Parse(balance, CultureInfo.InvariantCulture));
        using var response = await Send(HttpMethod.Post, "/api/v1/accounts", request);

        await AssertProblem(response, HttpStatusCode.BadRequest, AccountErrorCodes.InvalidOpeningBalance);
        await AssertOriginalAccounts();
    }

    public static IEnumerable<object[]> InvalidNames()
    {
        foreach (var rename in new[] { false, true })
        foreach (var name in new[] { "", "   ", new string('A', 101) })
            yield return new object[] { rename, name };
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task Invalid_name_returns_bad_request_without_changing_accounts(bool rename, string name)
    {
        object request = rename ? new RenameAccountRequest(name) : new CreateAccountRequest(name, 0);
        using var response = await Send(rename ? HttpMethod.Put : HttpMethod.Post,
            rename ? AccountPath(ownAccount.Id) : "/api/v1/accounts", request);

        await AssertProblem(response, HttpStatusCode.BadRequest, AccountErrorCodes.InvalidName);
        await AssertOriginalAccounts();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Null_name_returns_validation_problem_without_changing_accounts(bool rename)
    {
        using var response = await Send(rename ? HttpMethod.Put : HttpMethod.Post,
            rename ? AccountPath(ownAccount.Id) : "/api/v1/accounts", new { Name = (string?)null, OpeningBalance = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(body.RootElement.GetProperty("errors").EnumerateObject(),
            error => string.Equals(error.Name, "Name", StringComparison.OrdinalIgnoreCase) && error.Value.GetArrayLength() > 0);
        await AssertOriginalAccounts();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Name_at_maximum_length_is_accepted(bool rename)
    {
        var name = new string('A', 100);
        object request = rename ? new RenameAccountRequest(name) : new CreateAccountRequest(name, 0);
        using var response = await Send(rename ? HttpMethod.Put : HttpMethod.Post,
            rename ? AccountPath(ownAccount.Id) : "/api/v1/accounts", request);
        var saved = await ReadSuccess<AccountDto>(response);

        Assert.Equal(name, saved.Name);
        Assert.Contains(saved, await Accounts());
    }

    [Fact]
    public async Task Account_list_uses_authenticated_owner_even_with_foreign_user_id_in_query()
    {
        using var response = await Send(HttpMethod.Get, $"/api/v1/accounts?userId={stranger}");
        var accounts = await ReadSuccess<AccountDto[]>(response);

        Assert.Equal(ownAccount, Assert.Single(accounts));
        Assert.Equal(foreignAccount, Assert.Single(await Accounts(stranger)));
    }

    [Fact]
    public async Task Query_and_body_user_id_cannot_change_new_account_owner()
    {
        var request = new { Name = "Кошелёк", OpeningBalance = 25.50m, UserId = stranger };
        using var response = await Send(HttpMethod.Post, $"/api/v1/accounts?userId={stranger}", request);
        var saved = await ReadSuccess<AccountDto>(response);

        Assert.Contains(saved, await Accounts());
        Assert.Equal(foreignAccount, Assert.Single(await Accounts(stranger)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rename_changes_only_name_and_preserves_archive_state(bool archived)
    {
        if (archived)
        {
            using var archive = await Send(HttpMethod.Post, AccountPath(ownAccount.Id) + "/archive");
            await ReadSuccess<AccountDto>(archive);
        }

        using var response = await Send(HttpMethod.Put, AccountPath(ownAccount.Id), new RenameAccountRequest("  Карта  "));
        var renamed = await ReadSuccess<AccountDto>(response);

        Assert.Equal(ownAccount with { Name = "Карта", IsArchived = archived }, renamed);
        Assert.Equal(renamed, Assert.Single(await Accounts()));
        Assert.Equal(foreignAccount, Assert.Single(await Accounts(stranger)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Missing_or_foreign_account_cannot_be_renamed_or_archived(bool archive, bool foreign)
    {
        var id = foreign ? foreignAccount.Id : Guid.NewGuid();
        var path = AccountPath(id) + (archive ? "/archive" : "") + $"?userId={stranger}";
        using var response = await Send(archive ? HttpMethod.Post : HttpMethod.Put, path,
            archive ? null : new RenameAccountRequest("Новое имя"));

        await AssertProblem(response, HttpStatusCode.NotFound, AccountErrorCodes.NotFound);
        await AssertOriginalAccounts();
    }

    [Fact]
    public async Task Archive_preserves_history_and_rejects_new_operations()
    {
        using var scope = factory.Services.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionService>();
        var category = await transactions.CreateCategoryAsync(owner, new("Продукты", TransactionType.Expense));
        var from = new DateOnly(2026, 11, 1);
        var request = new CreateTransactionRequest(Guid.NewGuid(), ownAccount.Id, category.Id, TransactionType.Expense, 100, from, null);
        using var registration = await Send(HttpMethod.Post, "/api/v1/transactions", request);
        var saved = await ReadSuccess<TransactionDto>(registration);

        using var archive = await Send(HttpMethod.Post, AccountPath(ownAccount.Id) + "/archive");
        var archived = await ReadSuccess<AccountDto>(archive);
        Assert.Equal(ownAccount with { IsArchived = true }, archived);
        Assert.Equal(archived, Assert.Single(await Accounts()));

        using var rejected = await Send(HttpMethod.Post, "/api/v1/transactions", request with { RequestId = Guid.NewGuid() });
        await AssertProblem(rejected, HttpStatusCode.Conflict, AccountErrorCodes.Archived);
        using var history = await Send(HttpMethod.Get, "/api/v1/transactions?from=2026-11-01&toExclusive=2026-12-01");
        Assert.Equal(saved, Assert.Single(await ReadSuccess<TransactionDto[]>(history)));
        Assert.Equal(foreignAccount, Assert.Single(await Accounts(stranger)));
    }

    [Fact]
    public async Task Repeated_archiving_returns_same_account_without_duplicates()
    {
        var path = AccountPath(ownAccount.Id) + "/archive";
        using var first = await Send(HttpMethod.Post, path);
        var archived = await ReadSuccess<AccountDto>(first);
        using var repeated = await Send(HttpMethod.Post, path);

        Assert.Equal(ownAccount with { IsArchived = true }, archived);
        Assert.Equal(archived, await ReadSuccess<AccountDto>(repeated));
        Assert.Equal(archived, Assert.Single(await Accounts()));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("ARCHIVE")]
    public async Task Anonymous_requests_are_unauthorized_without_redirect_or_changes(string action)
    {
        var method = action == "GET" ? HttpMethod.Get : action == "PUT" ? HttpMethod.Put : HttpMethod.Post;
        var path = action is "GET" or "POST" ? "/api/v1/accounts" : AccountPath(ownAccount.Id);
        if (action == "ARCHIVE") path += "/archive";
        using var request = new HttpRequestMessage(method, path);
        if (action == "POST") request.Content = JsonContent.Create(new CreateAccountRequest("Наличные", 0));
        if (action == "PUT") request.Content = JsonContent.Create(new RenameAccountRequest("Новое имя"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        await AssertOriginalAccounts();
    }

    private async Task<AccountDto[]> Accounts(Guid? userId = null)
    {
        using var response = await Send(HttpMethod.Get, "/api/v1/accounts", userId: userId);
        return await ReadSuccess<AccountDto[]>(response);
    }

    private async Task AssertOriginalAccounts()
    {
        Assert.Equal(ownAccount, Assert.Single(await Accounts()));
        Assert.Equal(foreignAccount, Assert.Single(await Accounts(stranger)));
    }

    private static string AccountPath(Guid id) => $"/api/v1/accounts/{id}";

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, Guid? userId = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthenticationHandler.UserHeader, (userId ?? owner).ToString());
        if (body is not null) request.Content = JsonContent.Create(body, options: json);
        return await client.SendAsync(request);
    }

    private async Task<T> ReadSuccess<T>(HttpResponseMessage response) where T : class
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var result = await response.Content.ReadFromJsonAsync<T>(json);
        Assert.NotNull(result);
        return result;
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }
}
