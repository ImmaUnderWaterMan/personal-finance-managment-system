namespace PersonalFinance.AccountModule;

public sealed record Account(
    Guid Id,
    Guid UserId,
    string Name,
    decimal OpeningBalance,
    bool IsArchived);
