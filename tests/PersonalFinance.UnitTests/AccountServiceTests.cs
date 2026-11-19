using PersonalFinance.AccountModule;
using PersonalFinance.Contracts.Accounts;
using Xunit;

namespace PersonalFinance.UnitTests;

public class AccountServiceTests
{
    private readonly AccountService service = new(new InMemoryAccountRepository());
    private readonly Guid owner = Guid.NewGuid();

    [Fact]
    public async Task Create_preserves_balance_and_trims_name()
    {
        var created = await service.CreateAsync(owner, new("  Наличные  ", -50.25m));
        var saved = await service.GetAsync(owner, created.Id);

        Assert.Equal("Наличные", saved.Name);
        Assert.Equal(-50.25m, saved.OpeningBalance);
        Assert.False(saved.IsArchived);
        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(new ValidationResult(true, null), await service.ValidateAsync(new(owner, saved.Id)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Empty_name_is_rejected(string? name)
    {
        var exception = await Assert.ThrowsAsync<AccountException>(() => service.CreateAsync(owner, new(name!, 0)));
        Assert.Equal(AccountErrorCodes.InvalidName, exception.Code);
        Assert.Empty(await service.GetAllAsync(owner));
    }

    [Fact]
    public async Task Long_name_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<AccountException>(() => service.CreateAsync(owner, new(new string('a', 101), 0)));
        Assert.Equal(AccountErrorCodes.InvalidName, exception.Code);
    }

    [Fact]
    public async Task Invalid_balance_is_rejected_without_rounding()
    {
        foreach (var balance in new[] { 1.001m, 10000000000000000m, -10000000000000000m })
        {
            var exception = await Assert.ThrowsAsync<AccountException>(() => service.CreateAsync(owner, new("Счет", balance)));
            Assert.Equal(AccountErrorCodes.InvalidOpeningBalance, exception.Code);
        }

        Assert.Empty(await service.GetAllAsync(owner));
    }

    [Fact]
    public async Task Foreign_account_is_hidden_from_reads_and_writes()
    {
        var account = await service.CreateAsync(owner, new("Личный", 500));
        var stranger = Guid.NewGuid();

        Assert.Empty(await service.GetAllAsync(stranger));
        Assert.Equal(new ValidationResult(false, AccountErrorCodes.NotFound), await service.ValidateAsync(new(stranger, account.Id)));
        var read = await Assert.ThrowsAsync<AccountException>(() => service.GetAsync(stranger, account.Id));
        var rename = await Assert.ThrowsAsync<AccountException>(() => service.RenameAsync(stranger, account.Id, new("Чужой")));
        var archive = await Assert.ThrowsAsync<AccountException>(() => service.ArchiveAsync(stranger, account.Id));
        Assert.All(new[] { read, rename, archive }, e => Assert.Equal(AccountErrorCodes.NotFound, e.Code));
        Assert.Equal(account, await service.GetAsync(owner, account.Id));
    }

    [Fact]
    public async Task Missing_account_has_same_error_as_foreign_account()
    {
        var missing = Guid.NewGuid();
        Assert.Equal(new ValidationResult(false, AccountErrorCodes.NotFound), await service.ValidateAsync(new(owner, missing)));
        var exception = await Assert.ThrowsAsync<AccountException>(() => service.GetAsync(owner, missing));
        Assert.Equal(AccountErrorCodes.NotFound, exception.Code);
    }

    [Fact]
    public async Task Rename_preserves_id_and_balance()
    {
        var account = await service.CreateAsync(owner, new("Старое", 350.50m));
        var renamed = await service.RenameAsync(owner, account.Id, new("  Новое  "));

        Assert.Equal(account with { Name = "Новое" }, renamed);
        Assert.Equal(renamed, await service.GetAsync(owner, account.Id));
    }

    [Fact]
    public async Task Archive_blocks_validation_but_keeps_history_and_is_idempotent()
    {
        var account = await service.CreateAsync(owner, new("Основной", 100));
        var archived = await service.ArchiveAsync(owner, account.Id);

        Assert.True(archived.IsArchived);
        Assert.Equal(new ValidationResult(false, AccountErrorCodes.Archived), await service.ValidateAsync(new(owner, account.Id)));
        Assert.Equal(archived, await service.ArchiveAsync(owner, account.Id));
        Assert.Contains(archived, await service.GetAllAsync(owner));
    }

    [Fact]
    public async Task Concurrent_rename_and_archive_do_not_lose_archive_flag()
    {
        var account = await service.CreateAsync(owner, new("Основной", 10));
        await Task.WhenAll(
            Task.Run(() => service.RenameAsync(owner, account.Id, new("Новое"))),
            Task.Run(() => service.ArchiveAsync(owner, account.Id)));

        var result = await service.GetAsync(owner, account.Id);
        Assert.Equal("Новое", result.Name);
        Assert.True(result.IsArchived);
    }

    [Fact]
    public async Task Cancellation_prevents_creation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(owner, new("Счет", 0), cancellation.Token));
        Assert.Empty(await service.GetAllAsync(owner));
    }

    [Fact]
    public async Task Empty_user_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<AccountException>(() => service.CreateAsync(Guid.Empty, new("Счет", 0)));
        Assert.Equal(AccountErrorCodes.InvalidUser, exception.Code);
    }
}
