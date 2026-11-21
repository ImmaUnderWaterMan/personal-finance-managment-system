using System;
using System.Collections.Generic;

namespace PersonalFinance.Persistence.Entities;

public partial class Account
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = null!;

    public decimal OpeningBalance { get; set; }

    public bool IsArchived { get; set; }

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public virtual User User { get; set; } = null!;
}
