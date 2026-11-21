using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chubb.Claims.Api.Data;
using Chubb.Claims.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chubb.Claims.Tests;

public class ClaimsWorkflowTests
{
    private sealed class TestApp : WebApplicationFactory<Program>
    {
        private readonly string dbFile = Path.Combine(Path.GetTempPath(), $"chubb-tests-{Guid.NewGuid():N}.db");
        private readonly string environment;
        private readonly TimeProvider? clock;
        public TestApp(string environment = "Development", TimeProvider? clock = null) { this.environment = environment; this.clock = clock; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            if (clock is not null) builder.ConfigureServices(services => services.AddSingleton(clock));
            builder.UseSetting("ConnectionStrings:Claims", $"Data Source={dbFile};Foreign Keys=True");
        }
        public HttpClient Client(int? id = null)
        {
            var client = CreateClient();
            if (id.HasValue) client.DefaultRequestHeaders.Add("X-Demo-User-Id", id.Value.ToString());
            return client;
        }
        public async Task AddUsers()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            db.Users.AddRange(new User { Id = 3, DisplayName = "Other claimant", Role = UserRole.Claimant },
                new User { Id = 4, DisplayName = "Other officer", Role = UserRole.ClaimsOfficer });
            await db.SaveChangesAsync();
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(dbFile + suffix);
        }
    }
    private static readonly object ValidClaim = new { claimType = "Motor", incidentDescription = "Damage", incidentDate = "2026-01-02", claimedAmount = 12.34m };
    private static async Task<int> Submit(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/claims", ValidClaim);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }
    private static string PathFor(int id, string action = "") => $"/api/claims/{id}" + (action == "" ? "" : "/" + action);
    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task FullWorkflowPersistsHistoryAndEnforcesTerminalState()
    {
        using var app = new TestApp(); using var claimant = app.Client(1); using var officer = app.Client(2);
        var id = await Submit(claimant);
        Assert.Equal(HttpStatusCode.OK, (await officer.PostAsync(PathFor(id, "assign"), null)).StatusCode);
        var requested = await officer.PostAsJsonAsync(PathFor(id, "information-requests"), new { message = "Photo please" });
        Assert.Equal("InformationRequired", (await Json(requested)).GetProperty("status").GetString());
        var answered = await claimant.PostAsJsonAsync(PathFor(id, "respond"), new { message = "Photo sent" });
        Assert.Equal("UnderReview", (await Json(answered)).GetProperty("status").GetString());
        var approved = await officer.PostAsJsonAsync(PathFor(id, "decision"), new { decision = "Approve", reason = "Valid" });
        Assert.Equal("Approved", (await Json(approved)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await officer.PostAsJsonAsync(PathFor(id, "decision"), new { decision = "Reject", reason = "Late" })).StatusCode);
        var details = await Json(await claimant.GetAsync(PathFor(id)));
        Assert.Equal(12.34m, details.GetProperty("claimedAmount").GetDecimal());
        Assert.Equal(new[] { "Submitted", "Assigned", "InformationRequested", "InformationProvided", "Approved" },
            details.GetProperty("history").EnumerateArray().Select(x => x.GetProperty("action").GetString()).ToArray());
        var request = details.GetProperty("informationRequests")[0];
        Assert.Equal("Photo sent", request.GetProperty("response").GetString());
        Assert.Equal(2, details.GetProperty("history")[2].GetProperty("actorId").GetInt32());
        Assert.Equal(1, details.GetProperty("history")[3].GetProperty("actorId").GetInt32());
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        var persisted = await db.Claims.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(ClaimStatus.Approved, persisted.Status);
        Assert.Equal(TimeSpan.Zero, persisted.UpdatedAt.Offset);
    }

    [Fact]
    public async Task RejectionAndRoleVisibilityAreEnforced()
    {
        using var app = new TestApp(); using var owner = app.Client(1); using var officer = app.Client(2);
        await app.AddUsers(); using var other = app.Client(3); using var otherOfficer = app.Client(4);
        var id = await Submit(owner);
        Assert.Empty((await Json(await other.GetAsync("/api/claims"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(PathFor(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(PathFor(id, "respond"), new { message = "x" })).StatusCode);
        Assert.Single((await Json(await otherOfficer.GetAsync("/api/claims"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync(PathFor(id, "assign"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync(PathFor(id, "decision"), new { decision = "Approve", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync(PathFor(id, "information-requests"), new { message = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.PostAsJsonAsync("/api/claims", ValidClaim)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await officer.PostAsJsonAsync(PathFor(id, "decision"), new { decision = "Reject", reason = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await officer.PostAsync(PathFor(id, "assign"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOfficer.PostAsJsonAsync(PathFor(id, "decision"), new { decision = "Reject", reason = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOfficer.PostAsJsonAsync(PathFor(id, "information-requests"), new { message = "x" })).StatusCode);
        var rejected = await officer.PostAsJsonAsync(PathFor(id, "decision"), new { decision = "Reject", reason = "Not covered" });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("Rejected", (await Json(rejected)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await officer.PostAsJsonAsync(PathFor(id, "information-requests"), new { message = "More" })).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        Assert.Equal(ClaimStatus.Rejected, (await db.Claims.AsNoTracking().SingleAsync(x => x.Id == id)).Status);
    }

    [Fact]
    public async Task InputAndIdentityAreValidated()
    {
        using var app = new TestApp(); using var anonymous = app.Client(); using var claimant = app.Client(1); using var officer = app.Client(2);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/claims")).StatusCode);
        using var unknown = app.Client(999); Assert.Equal(HttpStatusCode.Unauthorized, (await unknown.GetAsync("/api/claims")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await claimant.PostAsJsonAsync("/api/claims", new { claimType = "Motor", incidentDescription = "x", incidentDate = "2026-01-01", claimedAmount = 0.001m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await claimant.PostAsJsonAsync("/api/claims", new { claimType = "Other", incidentDescription = "x", incidentDate = "2026-01-01", claimedAmount = 1 })).StatusCode);
        var id = await Submit(claimant);
        Assert.Equal(HttpStatusCode.Conflict, (await claimant.PostAsJsonAsync(PathFor(id, "respond"), new { message = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await officer.PostAsync(PathFor(id, "assign"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await officer.PostAsJsonAsync(PathFor(id, "decision"), new { decision = "Approve", reason = " " })).StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task InvalidDemoIdentityIsRejected(string id)
    {
        using var app = new TestApp(); using var client = app.Client();
        client.DefaultRequestHeaders.Add("X-Demo-User-Id", id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/claims")).StatusCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Theory]
    [InlineData("2026-06-01T15:59:59Z", "2026-05-31", HttpStatusCode.Created)]
    [InlineData("2026-06-01T15:59:59Z", "2026-06-01", HttpStatusCode.Created)]
    [InlineData("2026-06-01T15:59:59Z", "2026-06-02", HttpStatusCode.BadRequest)]
    [InlineData("2026-06-01T16:00:00Z", "2026-06-01", HttpStatusCode.Created)]
    [InlineData("2026-06-01T16:00:00Z", "2026-06-02", HttpStatusCode.Created)]
    [InlineData("2026-06-01T16:00:00Z", "2026-06-03", HttpStatusCode.BadRequest)]
    public async Task IncidentDateUsesMalaysiaCalendar(string instant, string date, HttpStatusCode expected)
    {
        using var app = new TestApp(clock: new FixedTimeProvider(DateTimeOffset.Parse(instant)));
        using var claimant = app.Client(1);
        var result = await claimant.PostAsJsonAsync("/api/claims", new { claimType = "Motor", incidentDescription = "Damage", incidentDate = date, claimedAmount = 1 });
        Assert.Equal(expected, result.StatusCode);
        if (expected == HttpStatusCode.BadRequest) Assert.Contains("future in Malaysia", await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DemoHeaderFailsClosedOutsideDevelopment()
    {
        using var app = new TestApp("Production"); using var client = app.Client(1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/claims")).StatusCode);
    }

    [Fact]
    public async Task CompetingOfficersCannotBothAssign()
    {
        using var app = new TestApp(); using var claimant = app.Client(1); using var a = app.Client(2);
        await app.AddUsers(); using var b = app.Client(4); var id = await Submit(claimant);
        var results = await Task.WhenAll(a.PostAsync(PathFor(id, "assign"), null), b.PostAsync(PathFor(id, "assign"), null));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var details = await Json(await claimant.GetAsync(PathFor(id)));
        Assert.Single(details.GetProperty("history").EnumerateArray(), h => h.GetProperty("action").GetString() == "Assigned");
    }
}
