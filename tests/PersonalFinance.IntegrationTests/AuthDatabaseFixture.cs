using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace PersonalFinance.IntegrationTests;

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PERSONAL_FINANCE_TEST_DB")))
            Skip = "Задайте PERSONAL_FINANCE_TEST_DB для проверки настоящего PostgreSQL.";
    }
}

public sealed class AuthDatabaseFixture : IAsyncLifetime
{
    private readonly string databaseName = $"personal_finance_auth_test_{Guid.NewGuid():N}";
    private string adminConnectionString = "";
    private bool created;
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("PERSONAL_FINANCE_TEST_DB");
        if (string.IsNullOrWhiteSpace(source)) return;

        var options = new NpgsqlConnectionStringBuilder(source) { Database = "postgres", Pooling = false };
        adminConnectionString = options.ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
            created = true;
        }

        try
        {
            options.Database = databaseName;
            ConnectionString = options.ConnectionString;
            await using var database = new NpgsqlConnection(ConnectionString);
            await database.OpenAsync();
            var scripts = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Database"), "*.sql")
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (scripts.Length != 5) throw new InvalidOperationException("Не найдены пять SQL-скриптов схемы базы данных.");
            foreach (var path in scripts)
            {
                await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(path), database);
                await command.ExecuteNonQueryAsync();
            }
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
        created = false;
        ConnectionString = "";
    }
}

internal sealed class AuthApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Finance"] = connectionString }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
    }
}
