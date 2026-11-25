using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Auth;
using PersonalFinance.Persistence.Data;
using PersonalFinance.Persistence.Entities;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public sealed class AuthApiTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>, IAsyncLifetime
{
    private const string Password = "CourseworkTest!42";
    private readonly string email = $"auth-test-{Guid.NewGuid():N}@test.invalid";
    private AuthApiFactory factory = null!;
    private HttpClient client = null!;

    public Task InitializeAsync()
    {
        factory = new AuthApiFactory(database.ConnectionString);
        client = CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
    }

    [PostgresFact]
    public async Task Registration_saves_hash_default_records_and_authenticated_session()
    {
        var request = new RegisterRequest("  Тестовый пользователь  ", $"  {email.ToUpperInvariant()}  ", Password);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        var user = await ReadUser(response);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(email, user.Email);
        Assert.Equal("Тестовый пользователь", user.DisplayName);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "displayName", "email", "id" },
            body.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith("PersonalFinance.Session=", StringComparison.Ordinal) &&
            cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
            cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var saved = await context.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.True(saved.IsActive);
        Assert.Equal(email, saved.Email);
        Assert.Equal(user.DisplayName, saved.DisplayName);
        Assert.NotEqual(Password, saved.Password);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(saved, saved.Password, Password));
        var account = Assert.Single(await context.Accounts.AsNoTracking().Where(item => item.UserId == user.Id).ToArrayAsync());
        Assert.Equal("Основной счёт", account.Name);
        Assert.Equal(0m, account.OpeningBalance);
        Assert.False(account.IsArchived);
        Assert.Equal(6, await context.Categories.CountAsync(item => item.UserId == user.Id && item.Type == 1));
        Assert.Equal(3, await context.Categories.CountAsync(item => item.UserId == user.Id && item.Type == 0));
        using var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(user, await ReadUser(me));
    }

    [PostgresTheory]
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("short-password")]
    [InlineData("long-password")]
    public async Task Invalid_registration_does_not_create_user_or_session(string invalidField)
    {
        var request = new RegisterRequest("Тестовый пользователь", email, Password);
        request = invalidField switch
        {
            "name" => request with { DisplayName = "   " },
            "email" => request with { Email = "invalid-email" },
            "short-password" => request with { Password = "short" },
            _ => request with { Password = new string('x', 129) }
        };
        var before = await Counts();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        await AssertProblem(response, HttpStatusCode.BadRequest);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(before, await Counts());
        using var me = await client.GetAsync("/api/v1/auth/me");
        AssertUnauthorized(me);
    }

    [PostgresFact]
    public async Task Duplicate_normalized_email_returns_conflict_without_extra_default_records()
    {
        var user = await Register();
        var before = await Counts();
        using var anonymous = CreateClient();
        var request = new RegisterRequest("Другой пользователь", $" {email.ToUpperInvariant()} ", Password);

        using var response = await anonymous.PostAsJsonAsync("/api/v1/auth/register", request);

        await AssertProblem(response, HttpStatusCode.Conflict);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(before, await Counts());
        using var me = await anonymous.GetAsync("/api/v1/auth/me");
        AssertUnauthorized(me);
        using var original = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(user, await ReadUser(original));
    }

    [PostgresFact]
    public async Task Login_accepts_normalized_email_and_restores_session_after_logout()
    {
        var user = await Register();
        await Logout();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new AuthRequest($"  {email.ToUpperInvariant()}  ", Password));

        Assert.Equal(user, await ReadUser(response));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("PersonalFinance.Session=", StringComparison.Ordinal));
        using var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(user, await ReadUser(me));
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wrong_password_or_unknown_email_does_not_create_session(bool unknownEmail)
    {
        await Register();
        await Logout();
        var before = await Counts();
        var address = unknownEmail ? $"unknown-{Guid.NewGuid():N}@test.invalid" : email;
        var password = unknownEmail ? Password : "WrongPassword!42";

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new AuthRequest(address, password));

        await AssertProblem(response, HttpStatusCode.Unauthorized);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Неверный email или пароль.", body.RootElement.GetProperty("title").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(before, await Counts());
        using var me = await client.GetAsync("/api/v1/auth/me");
        AssertUnauthorized(me);
    }

    [PostgresFact]
    public async Task Inactive_user_loses_existing_session_and_cannot_log_in()
    {
        var user = await Register();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var saved = await context.Users.SingleAsync(item => item.Id == user.Id);
            saved.IsActive = false;
            await context.SaveChangesAsync();
        }

        using var me = await client.GetAsync("/api/v1/auth/me");
        AssertUnauthorized(me);
        using var accounts = await client.GetAsync("/api/v1/accounts");
        AssertUnauthorized(accounts);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new AuthRequest(email, Password));
        await AssertProblem(login, HttpStatusCode.Unauthorized);
    }

    [PostgresFact]
    public async Task Logout_deletes_cookie_and_prevents_access_without_deleting_user()
    {
        var user = await Register();
        using var response = await client.PostAsync("/api/v1/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith("PersonalFinance.Session=", StringComparison.Ordinal) && cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        using var me = await client.GetAsync("/api/v1/auth/me");
        AssertUnauthorized(me);
        using var accounts = await client.GetAsync("/api/v1/accounts");
        AssertUnauthorized(accounts);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.True(await context.Users.AnyAsync(item => item.Id == user.Id && item.IsActive));
    }

    [PostgresTheory]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/accounts")]
    public async Task Anonymous_requests_are_unauthorized_without_redirect(string path)
    {
        using var response = await client.GetAsync(path);
        AssertUnauthorized(response);
    }

    [PostgresFact]
    public async Task Invalid_cookie_and_test_user_header_do_not_authenticate_request()
    {
        var user = await Register();
        using var invalid = CreateClient(handleCookies: false);
        invalid.DefaultRequestHeaders.Add("Cookie", "PersonalFinance.Session=not-a-valid-session");
        invalid.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, user.Id.ToString());

        using var response = await invalid.GetAsync("/api/v1/auth/me");

        AssertUnauthorized(response);
    }

    [PostgresFact]
    public async Task Separate_cookie_sessions_only_access_their_own_accounts()
    {
        var first = await Register();
        using var secondClient = CreateClient();
        using var registration = await secondClient.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("Второй пользователь", $"second-{Guid.NewGuid():N}@test.invalid", Password));
        var second = await ReadUser(registration);
        using var creation = await client.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest("Личный", 10));
        Assert.Equal(HttpStatusCode.OK, creation.StatusCode);
        using var firstAccounts = await client.GetAsync("/api/v1/accounts");
        firstAccounts.EnsureSuccessStatusCode();
        var ownAccounts = (await firstAccounts.Content.ReadFromJsonAsync<AccountDto[]>())!;
        Assert.Equal(2, ownAccounts.Length);
        using var secondAccounts = await secondClient.GetAsync($"/api/v1/accounts?userId={first.Id}");
        secondAccounts.EnsureSuccessStatusCode();
        var foreignAccount = Assert.Single((await secondAccounts.Content.ReadFromJsonAsync<AccountDto[]>())!);
        Assert.DoesNotContain(ownAccounts, account => account.Id == foreignAccount.Id);
        using var firstMe = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(first, await ReadUser(firstMe));
        using var secondMe = await secondClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(second, await ReadUser(secondMe));
    }

    private HttpClient CreateClient(bool handleCookies = true) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = handleCookies });

    private async Task<UserDto> Register()
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Тестовый пользователь", email, Password));
        return await ReadUser(response);
    }

    private async Task Logout()
    {
        using var response = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<(int Users, int Accounts, int Categories)> Counts()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        return (await context.Users.CountAsync(), await context.Accounts.CountAsync(), await context.Categories.CountAsync());
    }

    private static async Task<UserDto> ReadUser(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(user);
        return user;
    }

    private static void AssertUnauthorized(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
    }
}
