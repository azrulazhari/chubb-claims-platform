using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chubb.Claims.Api.Data;
using Chubb.Claims.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Chubb.Claims.Tests;

public class DashboardTests
{
    private sealed class TestApp(string environment = "Development") : WebApplicationFactory<Program>
    {
        private readonly string dbFile = Path.Combine(Path.GetTempPath(), $"chubb-dashboard-{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Claims", $"Data Source={dbFile};Foreign Keys=True");
        }
        public HttpClient Client(string? id = null)
        {
            var client = CreateClient();
            if (id is not null) client.DefaultRequestHeaders.Add("X-Demo-User-Id", id);
            return client;
        }
        public async Task Seed(params Claim[] claims)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            db.Users.Add(new User { Id = 3, DisplayName = "Second officer", Role = UserRole.ClaimsOfficer });
            db.Claims.AddRange(claims);
            await db.SaveChangesAsync();
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(dbFile + suffix);
        }
    }

    private static Claim Claim(string number, ClaimStatus status, decimal liability, int? officer = null) => new()
    {
        ClaimNumber = number, ClaimantId = 1, AssignedOfficerId = officer, ClaimType = ClaimType.Motor,
        IncidentDescription = "Damage", IncidentDate = new DateOnly(2026, 1, 1), ClaimedAmount = 1,
        EstimatedLiability = liability, Status = status
    };
    private static async Task<JsonElement> Dashboard(HttpClient client)
    {
        var response = await client.GetAsync("/api/reports/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task DashboardCountsOutstandingExcludesTerminalAndIncludesZeroWorkload()
    {
        using var app = new TestApp(); using var client = app.Client("2");
        await app.Seed(Claim("S", ClaimStatus.Submitted, .10m), Claim("U", ClaimStatus.UnderReview, .20m, 2),
            Claim("I", ClaimStatus.InfoRequested, .03m, 2), Claim("A", ClaimStatus.Approved, 100m, 2),
            Claim("R", ClaimStatus.Rejected, 200m), Claim("L", ClaimStatus.Settled, 300m, 3));
        var json = await Dashboard(client);
        Assert.Equal(6, json.GetProperty("totalClaimCount").GetInt64());
        Assert.Equal(3, json.GetProperty("outstandingCount").GetInt64());
        Assert.Equal(1, json.GetProperty("approvedCount").GetInt64());
        Assert.Equal(1, json.GetProperty("rejectedCount").GetInt64());
        Assert.Equal(1, json.GetProperty("unassignedOutstandingCount").GetInt64());
        Assert.Equal(.33m, json.GetProperty("outstandingEstimatedLiability").GetDecimal());
        Assert.Equal("MYR", json.GetProperty("currencyCode").GetString());
        var officers = json.GetProperty("officerWorkload");
        Assert.Equal(2, officers.GetArrayLength());
        Assert.Equal(2, officers[0].GetProperty("officerId").GetInt32());
        Assert.Equal("Demo Claims Officer", officers[0].GetProperty("displayName").GetString());
        Assert.Equal(2, officers[0].GetProperty("outstandingCount").GetInt64());
        Assert.Equal(3, officers[1].GetProperty("officerId").GetInt32());
        Assert.Equal(0, officers[1].GetProperty("outstandingCount").GetInt64());
    }

    [Fact]
    public async Task EmptyClaimsReturnZeroMetricsAndSeedOfficer()
    {
        using var app = new TestApp(); using var client = app.Client("2");
        var json = await Dashboard(client);
        foreach (var name in new[] { "totalClaimCount", "outstandingCount", "approvedCount", "rejectedCount", "unassignedOutstandingCount" })
            Assert.Equal(0, json.GetProperty(name).GetInt64());
        Assert.Equal(0m, json.GetProperty("outstandingEstimatedLiability").GetDecimal());
        Assert.Equal("MYR", json.GetProperty("currencyCode").GetString());
        Assert.Equal(0, json.GetProperty("officerWorkload")[0].GetProperty("outstandingCount").GetInt64());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("abc", HttpStatusCode.Unauthorized)]
    [InlineData("0", HttpStatusCode.Unauthorized)]
    [InlineData("999", HttpStatusCode.Unauthorized)]
    [InlineData("1", HttpStatusCode.Forbidden)]
    public async Task DashboardRequiresOfficerIdentity(string? identity, HttpStatusCode expected)
    {
        using var app = new TestApp(); using var client = app.Client(identity);
        Assert.Equal(expected, (await client.GetAsync("/api/reports/dashboard")).StatusCode);
    }

    [Fact]
    public async Task DemoIdentityFailsClosedOutsideDevelopment()
    {
        using var app = new TestApp("Production"); using var client = app.Client("2");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/reports/dashboard")).StatusCode);
    }

    [Fact]
    public async Task MaximumInt64SenIsExactAndOverflowReturnsExplicitError()
    {
        using var app = new TestApp(); using var client = app.Client("2");
        await app.Seed(Claim("MAX", ClaimStatus.Submitted, 92233720368547758.07m));
        Assert.Equal(92233720368547758.07m, (await Dashboard(client)).GetProperty("outstandingEstimatedLiability").GetDecimal());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            db.Claims.Add(Claim("EXTRA", ClaimStatus.InfoRequested, .01m));
            await db.SaveChangesAsync();
        }
        var response = await client.GetAsync("/api/reports/dashboard");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("exceeds the supported range", await response.Content.ReadAsStringAsync());
    }
}
