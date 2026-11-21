using System;
using System.Collections.Generic;

namespace PersonalFinance.Persistence.Entities;

public partial class Transaction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid RequestId { get; set; }

    public Guid AccountId { get; set; }

    public Guid CategoryId { get; set; }

    public int Type { get; set; }

    public decimal Amount { get; set; }

    public DateOnly OperationDate { get; set; }

    public string? Comment { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual Category Category { get; set; } = null!;
}
