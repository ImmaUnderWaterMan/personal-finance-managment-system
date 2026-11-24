using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("PERSONAL_FINANCE_API_URL") ?? "http://localhost:5080");
var connection = builder.Configuration.GetConnectionString("Finance")
    ?? Environment.GetEnvironmentVariable("PERSONAL_FINANCE_DB")
    ?? throw new InvalidOperationException("MISS CONNECTION STRING");
builder.Services.AddDbContext<FinanceDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<IAccountRepository, PostgresAccountRepository>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IAccountValidationService>(sp => sp.GetRequiredService<IAccountService>());
builder.Services.AddScoped<ITransactionRepository, PostgresTransactionRepository>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IExpenseQueryService>(sp => sp.GetRequiredService<ITransactionService>());
builder.Services.AddScoped<IBudgetRepository, PostgresBudgetRepository>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IPasswordHasher<PersonalFinance.Persistence.Entities.User>, PasswordHasher<PersonalFinance.Persistence.Entities.User>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "PersonalFinance.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnValidatePrincipal = async context =>
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var database = context.HttpContext.RequestServices.GetRequiredService<FinanceDbContext>();
        if (!Guid.TryParse(id, out var userId) ||
            !await database.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.IsActive, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "", Version = "v1" }));
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception exception) when (exception is AccountException or TransactionException or BudgetException or ReportException)
    {
        var code = exception switch
        {
            AccountException e => e.Code,
            TransactionException e => e.Code,
            BudgetException e => e.Code,
            ReportException e => e.Code,
            _ => "INVALID_REQUEST"
        };
        var status = code.EndsWith("NOT_FOUND") ? 404 : code is "REQUEST_CONFLICT" or "ACCOUNT_ARCHIVED" ? 409 : 400;
        await Results.Problem(statusCode: status, title: exception.Message, extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
    }
    catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
    {
        await Results.Problem(statusCode: 409, title: "Запись с такими данными уже существует.").ExecuteAsync(context);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Ошибка API {Path}", context.Request.Path);
        await Results.Problem(statusCode: 503, title: "Сервис временно недоступен. Повторите запрос позже.").ExecuteAsync(context);
    }
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Personal Finance API v1"));
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", async (FinanceDbContext database, CancellationToken cancellationToken) =>
    await database.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ok" })
        : Results.Problem(statusCode: 503, title: "Нет подключения к PostgreSQL."));
app.Run();

public partial class Program { }
