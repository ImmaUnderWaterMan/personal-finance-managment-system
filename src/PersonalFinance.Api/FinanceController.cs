using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Transactions;
using PersonalFinance.Contracts.Reports;

namespace PersonalFinance.Api;

[Authorize]
[ApiController]
[Route("api/v1")]
public sealed class FinanceController(IAccountService accounts, ITransactionService transactions, IBudgetService budgets, IReportService reports) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("accounts")]
    public Task<IReadOnlyList<AccountDto>> Accounts(CancellationToken ct) => accounts.GetAllAsync(UserId, ct);
    [HttpPost("accounts")]
    public Task<AccountDto> CreateAccount(CreateAccountRequest request, CancellationToken ct) => accounts.CreateAsync(UserId, request, ct);
    [HttpPut("accounts/{id:guid}")]
    public Task<AccountDto> RenameAccount(Guid id, RenameAccountRequest request, CancellationToken ct) => accounts.RenameAsync(UserId, id, request, ct);
    [HttpPost("accounts/{id:guid}/archive")]
    public Task<AccountDto> ArchiveAccount(Guid id, CancellationToken ct) => accounts.ArchiveAsync(UserId, id, ct);
    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => transactions.GetCategoriesAsync(UserId, ct);
    [HttpPost("categories")]
    public Task<CategoryDto> CreateCategory(CreateCategoryRequest request, CancellationToken ct) => transactions.CreateCategoryAsync(UserId, request, ct);
    [HttpGet("transactions")]
    public Task<IReadOnlyList<TransactionDto>> History(DateOnly from, DateOnly toExclusive, CancellationToken ct) => transactions.GetHistoryAsync(UserId, from, toExclusive, ct);
    [HttpPost("transactions")]
    public Task<TransactionDto> Register(CreateTransactionRequest request, CancellationToken ct) => transactions.RegisterAsync(UserId, request, ct);
    [HttpPut("budgets/{categoryId:guid}/{year:int}/{month:int}")]
    public Task<BudgetDto> SetBudget(Guid categoryId, int year, int month, SetBudgetRequest request, CancellationToken ct)
    {
        if (request.CategoryId != categoryId || request.Year != year || request.Month != month)
            throw new BudgetException(BudgetErrorCodes.InvalidPeriod, "Параметры адреса и запроса не совпадают.");
        return budgets.SetAsync(UserId, request, ct);
    }
    [HttpGet("budgets")]
    public Task<IReadOnlyList<BudgetReportDto>> Budgets(int year, int month, CancellationToken ct) => reports.GetMonthlyBudgetsAsync(UserId, year, month, ct);
    [HttpGet("reports/summary")]
    public Task<FinanceReportDto> Summary(DateOnly from, DateOnly toExclusive, CancellationToken ct) => reports.GetSummaryAsync(UserId, new(from, toExclusive), ct);
}
