using System;
using System.Collections.Generic;

namespace PersonalFinance.Persistence.Entities;

public partial class Budget
{
    public Guid UserId { get; set; }

    public Guid CategoryId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public decimal SpendingLimit { get; set; }

    public int CategoryType { get; set; }

    public virtual Category Category { get; set; } = null!;
}
