using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Transactions;

namespace PersonalFinance.TransactionModule;

public sealed class TransactionService(
    ITransactionRepository repository,
    IAccountValidationService accountValidation) : ITransactionService
{
    public async Task<CategoryDto> CreateCategoryAsync(
        Guid userId,
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        ArgumentNullException.ThrowIfNull(request);
        EnsureType(request.Type);
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
        {
            throw Error(TransactionErrorCodes.InvalidName, "Название категории должно содержать от 1 до 100 символов.");
        }

        var category = new Category(Guid.NewGuid(), userId, name, request.Type);
        await repository.AddCategoryAsync(category, cancellationToken);
        return ToDto(category);
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        var categories = await repository.GetCategoriesAsync(userId, cancellationToken);
        return categories.Select(ToDto).ToArray();
    }

    public async Task<TransactionDto> RegisterAsync(
        Guid userId,
        CreateTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty)
        {
            throw Error(TransactionErrorCodes.InvalidRequestId, "Укажите идентификатор запроса.");
        }

        var existing = await repository.FindByRequestIdAsync(userId, request.RequestId, cancellationToken);
        if (existing is not null)
        {
            return ResolveReplay(existing, request);
        }

        EnsureType(request.Type);
        if (request.Amount <= 0 || request.Amount > 9999999999999999.99m || decimal.Round(request.Amount, 2) != request.Amount)
        {
            throw Error(TransactionErrorCodes.InvalidAmount, "Укажите положительную сумму с точностью до копеек в пределах numeric(18,2).");
        }

        if (request.Comment?.Length > 150)
        {
            throw Error(TransactionErrorCodes.InvalidComment, "Комментарий должен содержать не более 150 символов.");
        }

        var validation = await accountValidation.ValidateAsync(new(userId, request.AccountId), cancellationToken);
        if (!validation.IsValid)
        {
            throw Error(validation.ErrorCode ?? AccountErrorCodes.NotFound, "Счет недоступен для регистрации операции.");
        }

        var category = await repository.GetCategoryAsync(userId, request.CategoryId, cancellationToken);
        if (category is null)
        {
            throw Error(TransactionErrorCodes.CategoryNotFound, "Категория не найдена.");
        }

        if (category.Type != request.Type)
        {
            throw Error(TransactionErrorCodes.InvalidCategoryType, "Тип категории не соответствует типу операции.");
        }

        var saved = await repository.AddOrGetAsync(new(Guid.NewGuid(), userId, request), cancellationToken);
        return ResolveReplay(saved, request);
    }

    public async Task<IReadOnlyList<TransactionDto>> GetHistoryAsync(
        Guid userId,
        DateOnly from,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUser(userId);
        if (from >= toExclusive)
        {
            throw Error(TransactionErrorCodes.InvalidPeriod, "Начало периода должно быть раньше конца.");
        }

        var history = await repository.GetHistoryAsync(userId, from, toExclusive, cancellationToken);
        return history.Select(ToDto).ToArray();
    }

    public async Task<ExpenseSummaryResult> GetTotalAsync(
        ExpenseSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureUser(request.UserId);
        if (request.From >= request.ToExclusive)
        {
            return new(false, null, TransactionErrorCodes.InvalidPeriod);
        }

        var category = await repository.GetCategoryAsync(request.UserId, request.CategoryId, cancellationToken);
        if (category is null)
        {
            return new(false, null, TransactionErrorCodes.CategoryNotFound);
        }

        if (category.Type != TransactionType.Expense)
        {
            return new(false, null, TransactionErrorCodes.InvalidCategoryType);
        }

        var history = await repository.GetHistoryAsync(request.UserId, request.From, request.ToExclusive, cancellationToken);
        var total = history.Where(transaction => transaction.Request.CategoryId == request.CategoryId &&
            transaction.Request.Type == TransactionType.Expense).Sum(transaction => transaction.Request.Amount);
        return new(true, total, null);
    }

    private static TransactionDto ResolveReplay(FinancialTransaction existing, CreateTransactionRequest request)
    {
        if (existing.Request != request)
        {
            throw Error(TransactionErrorCodes.RequestConflict, "Идентификатор запроса уже использован для другой операции.");
        }

        return ToDto(existing);
    }

    private static void EnsureUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw Error(TransactionErrorCodes.InvalidUser, "Пользователь не определен.");
        }
    }

    private static void EnsureType(TransactionType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw Error(TransactionErrorCodes.InvalidType, "Неизвестный тип операции.");
        }
    }

    private static TransactionException Error(string code, string message) => new(code, message);

    private static CategoryDto ToDto(Category category) => new(category.Id, category.Name, category.Type);

    private static TransactionDto ToDto(FinancialTransaction transaction) => new(
        transaction.Id,
        transaction.Request.AccountId,
        transaction.Request.CategoryId,
        transaction.Request.Type,
        transaction.Request.Amount,
        transaction.Request.OperationDate,
        transaction.Request.Comment);
}
