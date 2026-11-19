using PersonalFinance.Contracts.Transactions;

namespace PersonalFinance.TransactionModule;

public sealed record Category(Guid Id, Guid UserId, string Name, TransactionType Type);

public sealed record FinancialTransaction(Guid Id, Guid UserId, CreateTransactionRequest Request);
