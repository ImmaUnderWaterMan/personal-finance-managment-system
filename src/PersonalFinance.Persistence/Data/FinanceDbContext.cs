using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Persistence.Entities;

namespace PersonalFinance.Persistence.Data;

public partial class FinanceDbContext : DbContext
{
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Account> Accounts { get; set; }

    public virtual DbSet<Budget> Budgets { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_accounts");

            entity.ToTable("accounts");

            entity.HasIndex(e => e.UserId, "ix_accounts_user_id");

            entity.HasIndex(e => new { e.Id, e.UserId }, "uq_accounts_id_user").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.OpeningBalance)
                .HasPrecision(18, 2)
                .HasColumnName("opening_balance");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.Accounts)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_accounts_users");
        });

        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.CategoryId, e.Year, e.Month }).HasName("pk_budgets");

            entity.ToTable("budgets");

            entity.HasIndex(e => new { e.CategoryId, e.UserId }, "ix_budgets_category_user");

            entity.HasIndex(e => new { e.UserId, e.Year, e.Month }, "ix_budgets_user_period");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.Year).HasColumnName("year");
            entity.Property(e => e.Month).HasColumnName("month");
            entity.Property(e => e.CategoryType)
                .HasDefaultValue(1)
                .HasColumnName("category_type");
            entity.Property(e => e.SpendingLimit)
                .HasPrecision(18, 2)
                .HasColumnName("spending_limit");

            entity.HasOne(d => d.Category).WithMany(p => p.Budgets)
                .HasPrincipalKey(p => new { p.Id, p.UserId, p.Type })
                .HasForeignKey(d => new { d.CategoryId, d.UserId, d.CategoryType })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_budgets_categories");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_categories");

            entity.ToTable("categories");

            entity.HasIndex(e => e.UserId, "ix_categories_user_id");

            entity.HasIndex(e => new { e.Id, e.UserId, e.Type }, "uq_categories_id_user_type").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.Categories)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_categories_users");
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_transactions");

            entity.ToTable("transactions");

            entity.HasIndex(e => new { e.AccountId, e.UserId }, "ix_transactions_account_user");

            entity.HasIndex(e => new { e.CategoryId, e.UserId, e.OperationDate }, "ix_transactions_category_user_date");

            entity.HasIndex(e => new { e.UserId, e.OperationDate }, "ix_transactions_user_date");

            entity.HasIndex(e => new { e.UserId, e.RequestId }, "uq_transactions_user_request").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.Comment)
                .HasMaxLength(150)
                .HasColumnName("comment");
            entity.Property(e => e.OperationDate).HasColumnName("operation_date");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Account).WithMany(p => p.Transactions)
                .HasPrincipalKey(p => new { p.Id, p.UserId })
                .HasForeignKey(d => new { d.AccountId, d.UserId })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_transactions_accounts");

            entity.HasOne(d => d.Category).WithMany(p => p.Transactions)
                .HasPrincipalKey(p => new { p.Id, p.UserId, p.Type })
                .HasForeignKey(d => new { d.CategoryId, d.UserId, d.Type })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_transactions_categories");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_users");

            entity.ToTable("users");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(100)
                .HasColumnName("display_name");
            entity.Property(e => e.Email)
                .HasMaxLength(256)
                .HasColumnName("email");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Password).HasColumnName("password");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
