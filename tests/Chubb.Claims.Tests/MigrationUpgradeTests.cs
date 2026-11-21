using Chubb.Claims.Api.Data;
using Chubb.Claims.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Chubb.Claims.Tests;

public class MigrationUpgradeTests
{
    private const string Initial = "20261121094757_InitialClaims";

    [Fact]
    public async Task UpgradePreservesLegacyClaimsChildrenConstraintsAndIndexes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new ClaimsDbContext(new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync(Initial);
        await DevelopmentSeed.InitializeAsync(db);
        var old = new Claim { ClaimNumber = "OLD", ClaimantId = 1, AssignedOfficerId = 2, ClaimType = ClaimType.Motor,
            IncidentDescription = "Legacy claim", IncidentDate = new DateOnly(2025, 1, 1), ClaimedAmount = 12.34m, Status = ClaimStatus.Settled };
        old.History.Add(new ClaimHistory { ActorId = 2, Action = "Legacy", Notes = "Preserve" });
        old.InformationRequests.Add(new InformationRequest { RequestedByOfficerId = 2, Message = "Evidence", Response = "Provided", RespondedAt = DateTimeOffset.UtcNow });
        db.Claims.Add(old);
        db.Claims.Add(new Claim { ClaimNumber = "OPEN", ClaimantId = 1, ClaimType = ClaimType.Property, IncidentDescription = "Open",
            IncidentDate = new DateOnly(2025, 2, 1), ClaimedAmount = 1m });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();
        var saved = await db.Claims.Include(x => x.History).Include(x => x.InformationRequests).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.Equal(ClaimStatus.Settled, saved[0].Status);
        Assert.Equal(12.34m, saved[0].ClaimedAmount);
        Assert.Equal("Preserve", Assert.Single(saved[0].History).Notes);
        Assert.Equal("Provided", Assert.Single(saved[0].InformationRequests).Response);
        Assert.Equal(ClaimStatus.Submitted, saved[1].Status);
        Assert.Equal(2, saved[0].AssignedOfficerId);
        Assert.Equal(1, saved[0].ClaimantId);
        using var fkCheck = connection.CreateCommand();
        fkCheck.CommandText = "PRAGMA foreign_key_check";
        await using (var reader = await fkCheck.ExecuteReaderAsync()) Assert.False(await reader.ReadAsync());
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT count(*) FROM sqlite_schema WHERE type='index' AND name IN ('IX_Claims_AssignedOfficerId','IX_Claims_ClaimantId','IX_Claims_ClaimNumber','IX_ClaimHistory_ActorId','IX_ClaimHistory_ClaimId','IX_InformationRequests_ClaimId','IX_InformationRequests_RequestedByOfficerId')";
        Assert.Equal(7L, (long)(await schema.ExecuteScalarAsync())!);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Claims SET Status = 'Invalid' WHERE ClaimNumber = 'OLD'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Claims SET CurrencyCode = 'SGD' WHERE ClaimNumber = 'OLD'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Claims SET ClaimedAmountSen = 0 WHERE ClaimNumber = 'OLD'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE ClaimHistory SET ClaimId = 999 WHERE Action = 'Legacy'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE InformationRequests SET ClaimId = 999 WHERE Message = 'Evidence'"));
        await db.Database.ExecuteSqlRawAsync("UPDATE Claims SET Status = 'Approved' WHERE ClaimNumber = 'OPEN'");
        db.ChangeTracker.Clear();
        Assert.Equal(ClaimStatus.Approved, (await db.Claims.SingleAsync(x => x.ClaimNumber == "OPEN")).Status);
        Assert.Equal(ClaimStatus.Settled, (await db.Claims.SingleAsync(x => x.ClaimNumber == "OLD")).Status);
    }

    [Fact]
    public async Task UpgradeFailureRollsBackAllSchemaAndDataChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new ClaimsDbContext(new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync(Initial);
        await DevelopmentSeed.InitializeAsync(db);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Claims (ClaimNumber, ClaimantId, ClaimType, IncidentDescription, IncidentDate, MarketCode, CurrencyCode, ClaimedAmountSen, EstimatedLiabilitySen, Status, CreatedAt, UpdatedAt) VALUES ('OLD', 1, 'Motor', 'Test', '2025-01-01', 'MY', 'MYR', 100, 0, 'Settled', '2025-01-01', '2025-01-01')");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ClaimHistory (ClaimId, ActorId, Action, CreatedAt) VALUES (999, 1, 'Invalid legacy child', '2025-01-01')");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON");
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.MigrateAsync());
        Assert.Equal("Settled", (await db.Claims.AsNoTracking().SingleAsync()).Status.ToString());
        Assert.Equal(1, await db.ClaimHistory.CountAsync());
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Claims SET Status = 'Approved'"));
    }
}
