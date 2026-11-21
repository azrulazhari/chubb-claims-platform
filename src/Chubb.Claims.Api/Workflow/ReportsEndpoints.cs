using Chubb.Claims.Api.Data;
using Chubb.Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Chubb.Claims.Api.Workflow;

public record OfficerWorkloadResponse(int OfficerId, string DisplayName, long OutstandingCount);
public record DashboardResponse(long TotalClaimCount, long OutstandingCount, long ApprovedCount, long RejectedCount,
    long UnassignedOutstandingCount, decimal OutstandingEstimatedLiability, string CurrencyCode,
    IReadOnlyList<OfficerWorkloadResponse> OfficerWorkload);

public static class ReportsEndpoints
{
    public static void MapReportsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reports");
        group.AddEndpointFilter(DemoIdentity.Filter);
        group.MapGet("/dashboard", Dashboard);
    }

    private static async Task<IResult> Dashboard(HttpContext http, ClaimsDbContext db, CancellationToken cancellationToken)
    {
        if (DemoIdentity.Actor(http).Role != UserRole.ClaimsOfficer) return Results.StatusCode(403);

        // One read transaction keeps counts, workload, and amounts from the same database snapshot.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var officers = await db.Users.AsNoTracking().Where(x => x.Role == UserRole.ClaimsOfficer)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.DisplayName }).ToListAsync(cancellationToken);
        var workload = officers.ToDictionary(x => x.Id, _ => 0L);
        long total = 0, outstanding = 0, approved = 0, rejected = 0, unassigned = 0, liabilitySen = 0;

        // Read the actual integer-sen column; SQL SUM can overflow and floating-point SUM is inexact.
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT Status, AssignedOfficerId, EstimatedLiabilitySen FROM Claims";
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                total = checked(total + 1);
                var status = reader.GetString(0);
                if (status == nameof(ClaimStatus.Approved)) approved = checked(approved + 1);
                if (status == nameof(ClaimStatus.Rejected)) rejected = checked(rejected + 1);
                if (status is not (nameof(ClaimStatus.Submitted) or nameof(ClaimStatus.UnderReview) or nameof(ClaimStatus.InfoRequested))) continue;
                outstanding = checked(outstanding + 1);
                liabilitySen = checked(liabilitySen + reader.GetInt64(2));
                if (reader.IsDBNull(1)) unassigned = checked(unassigned + 1);
                else
                {
                    var officerId = reader.GetInt32(1);
                    if (workload.ContainsKey(officerId)) workload[officerId] = checked(workload[officerId] + 1);
                }
            }
        }
        catch (OverflowException)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Dashboard total exceeds the supported range");
        }
        var result = new DashboardResponse(total, outstanding, approved, rejected, unassigned, liabilitySen / 100m, "MYR",
            officers.Select(x => new OfficerWorkloadResponse(x.Id, x.DisplayName, workload[x.Id])).ToList());
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(result);
    }
}
