using Chubb.Claims.Api.Data;
using Chubb.Claims.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Tests;

public class PersistenceTests
{
    private static async Task<(SqliteConnection Connection, ClaimsDbContext Db)> CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var db = new ClaimsDbContext(new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        await DevelopmentSeed.InitializeAsync(db);
        await DevelopmentSeed.InitializeAsync(db); // Repeated startup must not duplicate demo users.
        return (connection, db);
    }

    private static Claim NewClaim(string number = "CL-001") => new()
    {
        ClaimNumber = number, ClaimantId = 1, AssignedOfficerId = 2,
        ClaimType = ClaimType.Motor, IncidentDescription = "Vehicle damage",
        IncidentDate = new DateOnly(2026, 1, 1), ClaimedAmount = 123.45m,
        EstimatedLiability = 100.25m
    };

    [Fact]
    public async Task MigrationSeedsDemoUsersAndPersistsRelationshipsAndMoney()
    {
        var (connection, db) = await CreateDatabase();
        await using (connection)
        await using (db)
        {
            Assert.Equal(2, await db.Users.CountAsync());
            var claim = NewClaim();
            claim.History.Add(new ClaimHistory { ActorId = 1, Action = "Submitted" });
            claim.InformationRequests.Add(new InformationRequest { RequestedByOfficerId = 2, Message = "Please provide details" });
            db.Claims.Add(claim);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var saved = await db.Claims.Include(x => x.Claimant).Include(x => x.AssignedOfficer)
                .Include(x => x.History).Include(x => x.InformationRequests).SingleAsync();
            Assert.Equal(123.45m, saved.ClaimedAmount);
            Assert.Equal(100.25m, saved.EstimatedLiability);
            Assert.Equal(UserRole.Claimant, saved.Claimant.Role);
            Assert.Equal(UserRole.ClaimsOfficer, saved.AssignedOfficer!.Role);
            Assert.Single(saved.History);
            Assert.Single(saved.InformationRequests);
            Assert.Null(saved.InformationRequests.Single().Response);
            Assert.Equal(TimeSpan.Zero, saved.CreatedAt.Offset);
        }
    }

    [Fact]
    public async Task ForeignKeysRejectUnknownClaimant()
    {
        var (connection, db) = await CreateDatabase();
        await using (connection)
        await using (db)
        {
            var claim = NewClaim(); claim.ClaimantId = 999;
            db.Claims.Add(claim);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task DuplicateClaimNumberIsRejected()
    {
        var (connection, db) = await CreateDatabase();
        await using (connection)
        await using (db)
        {
            db.Claims.Add(NewClaim());
            await db.SaveChangesAsync();
            db.Claims.Add(NewClaim());
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public async Task InvalidAmountsAreRejected(int amount, int liability)
    {
        var (connection, db) = await CreateDatabase();
        await using (connection)
        await using (db)
        {
            var claim = NewClaim(); claim.ClaimedAmount = amount; claim.EstimatedLiability = liability;
            db.Claims.Add(claim);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyDescriptionIsRejected(string description)
    {
        var (connection, db) = await CreateDatabase();
        await using (connection)
        await using (db)
        {
            var claim = NewClaim(); claim.IncidentDescription = description;
            db.Claims.Add(claim);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
    [Theory]
    [InlineData("SG", "MYR")]
    [InlineData("MY", "SGD")]
    [InlineData("my", "MYR")]
    public async Task UnsupportedMarketOrCurrencyIsRejected(string market, string currency)
    {
        var (connection, db) = await CreateDatabase();
        await using (connection)
        await using (db)
        {
            var claim = NewClaim(); claim.MarketCode = market; claim.CurrencyCode = currency;
            db.Claims.Add(claim);
            await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task DatabaseRejectsUnsupportedCodesAndNonIntegerMoney()
    {
        var (connection, db) = await CreateDatabase();
        await using (connection)
        await using (db)
        {
            db.Claims.Add(NewClaim());
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Claims SET MarketCode = 'SG'"));
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Claims SET CurrencyCode = 'SGD'"));
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Claims SET EstimatedLiabilitySen = 'invalid'"));
        }
    }

    [Theory]
    [InlineData("0.001", "0")]
    [InlineData("1", "0.001")]
    [InlineData("92233720368547758.08", "0")]
    [InlineData("1", "92233720368547758.08")]
    public async Task FractionalSenAndOverflowAreRejected(string amount, string liability)
    {
        var (connection, db) = await CreateDatabase();
        await using (connection)
        await using (db)
        {
            var claim = NewClaim();
            claim.ClaimedAmount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
            claim.EstimatedLiability = decimal.Parse(liability, System.Globalization.CultureInfo.InvariantCulture);
            db.Claims.Add(claim);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task MoneyRoundTripsAndSqliteSumsExactIntegerSen()
    {
        var (connection, db) = await CreateDatabase();
        await using (connection)
        await using (db)
        {
            var first = NewClaim("CL-010"); first.ClaimedAmount = 0.10m; first.EstimatedLiability = 0m;
            var second = NewClaim("CL-020"); second.ClaimedAmount = 0.20m; second.EstimatedLiability = 92233720368547758.07m;
            db.Claims.AddRange(first, second);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var claims = await db.Claims.OrderBy(x => x.ClaimNumber).ToListAsync();
            Assert.Equal(0.10m, claims[0].ClaimedAmount);
            Assert.Equal(0.20m, claims[1].ClaimedAmount);
            Assert.Equal(92233720368547758.07m, claims[1].EstimatedLiability);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SUM(ClaimedAmountSen), typeof(SUM(ClaimedAmountSen)) FROM Claims";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(30L, reader.GetInt64(0));
            Assert.Equal("integer", reader.GetString(1));
            Assert.Equal(0.30m, reader.GetInt64(0) / 100m);
        }
    }

}
