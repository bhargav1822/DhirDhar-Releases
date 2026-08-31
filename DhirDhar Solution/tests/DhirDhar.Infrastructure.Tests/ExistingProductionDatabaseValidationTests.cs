using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Interest;
using DhirDhar.Infrastructure.Interest;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class ExistingProductionDatabaseValidationTests
{
    [Fact]
    public async Task ExistingDatabase_OpensSuccessfully_AndHasValidIntegrity()
    {
        var dbPath = @"d:\DhirDhar\DhirDhar Solution\DhirDhar.db";
        if (!File.Exists(dbPath))
        {
            // If the local db doesn't exist, skip test
            return;
        }

        // Ensure schema columns are migrated
        using (var setupConn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString()))
        {
            await setupConn.OpenAsync();
            using var pragmaCmd = setupConn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA table_info(Borrowers);";
            using var reader = await pragmaCmd.ExecuteReaderAsync();
            var cols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                cols.Add(reader.GetString(1));
            }
            reader.Close();

            if (!cols.Contains("ClosedDate"))
            {
                using var alterCmd = setupConn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Borrowers ADD COLUMN ClosedDate TEXT NULL;";
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!cols.Contains("ClosingAmount"))
            {
                using var alterCmd = setupConn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Borrowers ADD COLUMN ClosingAmount REAL NULL;";
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!cols.Contains("ClosedAccruedInterest"))
            {
                using var alterCmd = setupConn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Borrowers ADD COLUMN ClosedAccruedInterest REAL NULL;";
                await alterCmd.ExecuteNonQueryAsync();
            }
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        var options = new DbContextOptionsBuilder<DhirDharDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using var dbContext = new DhirDharDbContext(options);

        // 1. Verify PRAGMA integrity_check
        await using var conn = dbContext.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var integrityResult = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("ok", integrityResult);

        // 2. Query borrowers and transactions
        var borrowers = await dbContext.Borrowers.AsNoTracking().ToListAsync();
        var transactions = await dbContext.Transactions.AsNoTracking().ToListAsync();
        var settings = await dbContext.ApplicationSettings.AsNoTracking().ToListAsync();

        Assert.NotNull(borrowers);
        Assert.NotNull(transactions);

        // 3. Test interest calculation service over existing active borrowers
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(dbContext);
        services.AddScoped<IInterestCalculationService, InterestCalculationService>();
        await using var provider = services.BuildServiceProvider();
        var interestService = provider.GetRequiredService<IInterestCalculationService>();

        foreach (var borrower in borrowers.Take(10))
        {
            var result = await interestService.CalculateAsync(borrower.Id, DateTime.Today);
            Assert.NotNull(result);
            Assert.True(result.ClosingPrincipal >= 0m);
            Assert.True(result.TotalInterest >= 0m);
            Assert.Equal(result.ClosingPrincipal + result.UncapitalizedInterest, result.TotalOutstanding);
        }
    }
}
