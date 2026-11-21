using Chubb.Claims.Api.Data;
using Chubb.Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Api.Workflow;

public record SubmitClaimRequest(string? ClaimType, string? IncidentDescription, DateOnly? IncidentDate, decimal? ClaimedAmount);
public record MessageRequest(string? Message);
public record DecisionRequest(string? Decision, string? Reason);
public record HistoryResponse(int ActorId, string Action, string? Notes, DateTimeOffset CreatedAt);
public record InformationRequestResponse(int Id, int RequestedByOfficerId, string Message, string? Response, DateTimeOffset RequestedAt, DateTimeOffset? RespondedAt);
public record ClaimResponse(int Id, string ClaimNumber, int ClaimantId, int? AssignedOfficerId, string ClaimType,
    string IncidentDescription, DateOnly IncidentDate, string MarketCode, string CurrencyCode, decimal ClaimedAmount,
    decimal EstimatedLiability, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<HistoryResponse> History, IReadOnlyList<InformationRequestResponse> InformationRequests);

// Stored status names are retained for compatibility with the committed schema.
internal static class ClaimTransitions
{
    public static ClaimStatus? Next(ClaimStatus current, string action) => (current, action) switch
    {
        (ClaimStatus.Submitted, "Assigned") => ClaimStatus.UnderReview,
        (ClaimStatus.UnderReview, "InformationRequested") => ClaimStatus.InfoRequested,
        (ClaimStatus.InfoRequested, "InformationProvided") => ClaimStatus.UnderReview,
        (ClaimStatus.UnderReview, "Approved") => ClaimStatus.Approved,
        (ClaimStatus.UnderReview, "Rejected") => ClaimStatus.Rejected,
        _ => null
    };
    public static string Display(ClaimStatus status) => status switch
    {
        ClaimStatus.InfoRequested => "InformationRequired",
        _ => status.ToString()
    };
}

public static class ClaimsEndpoints
{
    public static void MapClaimsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/claims");
        group.AddEndpointFilter(DemoIdentity.Filter);
        group.MapPost("/", Submit);
        group.MapGet("/", List);
        group.MapGet("/{id:int}", Get);
        group.MapPost("/{id:int}/assign", Assign);
        group.MapPost("/{id:int}/information-requests", RequestInformation);
        group.MapPost("/{id:int}/respond", Respond);
        group.MapPost("/{id:int}/decision", Decide);
    }

    private static User Actor(HttpContext http) => DemoIdentity.Actor(http);
    private static IQueryable<Claim> Visible(ClaimsDbContext db, User user) => user.Role == UserRole.Claimant
        ? db.Claims.Where(x => x.ClaimantId == user.Id) : db.Claims;
    private static bool ValidText(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max;
    private static bool ValidAmount(decimal? value) => value is > 0 and <= 92233720368547758.07m && value.Value * 100 == decimal.Truncate(value.Value * 100);
    private static ClaimResponse ToResponse(Claim c) => new(c.Id, c.ClaimNumber, c.ClaimantId, c.AssignedOfficerId,
        c.ClaimType.ToString(), c.IncidentDescription, c.IncidentDate, c.MarketCode, c.CurrencyCode, c.ClaimedAmount,
        c.EstimatedLiability, ClaimTransitions.Display(c.Status), c.CreatedAt, c.UpdatedAt,
        c.History.OrderBy(x => x.Id).Select(x => new HistoryResponse(x.ActorId, x.Action, x.Notes, x.CreatedAt)).ToList(),
        c.InformationRequests.OrderBy(x => x.Id).Select(x => new InformationRequestResponse(x.Id, x.RequestedByOfficerId, x.Message, x.Response, x.RequestedAt, x.RespondedAt)).ToList());
    private static IQueryable<Claim> Details(IQueryable<Claim> claims) => claims.AsNoTracking().Include(x => x.History).Include(x => x.InformationRequests);

    private static async Task<IResult> Submit(SubmitClaimRequest request, HttpContext http, ClaimsDbContext db)
    {
        var actor = Actor(http);
        if (actor.Role != UserRole.Claimant) return Results.StatusCode(403);
        if ((request.ClaimType != "Motor" && request.ClaimType != "Property") || !ValidText(request.IncidentDescription, 2000) ||
            request.IncidentDate is null || request.IncidentDate == default(DateOnly) || !ValidAmount(request.ClaimedAmount))
            return Results.BadRequest(new { error = "Provide a valid claim type, description, incident date and positive MYR amount in whole sen." });
        var now = DateTimeOffset.UtcNow;
        var claim = new Claim { ClaimNumber = $"CL-{Guid.NewGuid():N}", ClaimantId = actor.Id,
            ClaimType = Enum.Parse<ClaimType>(request.ClaimType), IncidentDescription = request.IncidentDescription!,
            IncidentDate = request.IncidentDate.Value, ClaimedAmount = request.ClaimedAmount!.Value,
            EstimatedLiability = 0, CreatedAt = now, UpdatedAt = now };
        claim.History.Add(new ClaimHistory { ActorId = actor.Id, Action = "Submitted", CreatedAt = now });
        db.Claims.Add(claim);
        await db.SaveChangesAsync();
        return Results.Created($"/api/claims/{claim.Id}", ToResponse(claim));
    }

    private static async Task<IResult> List(HttpContext http, ClaimsDbContext db) =>
        Results.Ok((await Details(Visible(db, Actor(http))).OrderBy(x => x.Id).ToListAsync()).Select(ToResponse));
    private static async Task<IResult> Get(int id, HttpContext http, ClaimsDbContext db)
    {
        var claim = await Details(Visible(db, Actor(http))).SingleOrDefaultAsync(x => x.Id == id);
        return claim is null ? Results.NotFound() : Results.Ok(ToResponse(claim));
    }
    private static async Task<IResult> Assign(int id, HttpContext http, ClaimsDbContext db)
    {
        var actor = Actor(http);
        if (actor.Role != UserRole.ClaimsOfficer) return Results.StatusCode(403);
        return await Transition(id, actor, db, ClaimStatus.Submitted, "Assigned", null);
    }
    private static async Task<IResult> RequestInformation(int id, MessageRequest request, HttpContext http, ClaimsDbContext db)
    {
        var actor = Actor(http);
        if (actor.Role != UserRole.ClaimsOfficer) return Results.StatusCode(403);
        if (!ValidText(request.Message, 2000)) return Results.BadRequest(new { error = "A message of at most 2000 characters is required." });
        return await Transition(id, actor, db, ClaimStatus.UnderReview, "InformationRequested", request.Message);
    }
    private static async Task<IResult> Respond(int id, MessageRequest request, HttpContext http, ClaimsDbContext db)
    {
        var actor = Actor(http);
        if (actor.Role != UserRole.Claimant) return Results.StatusCode(403);
        if (!ValidText(request.Message, 2000)) return Results.BadRequest(new { error = "A response of at most 2000 characters is required." });
        return await Transition(id, actor, db, ClaimStatus.InfoRequested, "InformationProvided", request.Message);
    }
    private static async Task<IResult> Decide(int id, DecisionRequest request, HttpContext http, ClaimsDbContext db)
    {
        var actor = Actor(http);
        if (actor.Role != UserRole.ClaimsOfficer) return Results.StatusCode(403);
        if ((request.Decision != "Approve" && request.Decision != "Reject") || !ValidText(request.Reason, 2000))
            return Results.BadRequest(new { error = "Decision must be Approve or Reject and a reason is required." });
        return await Transition(id, actor, db, ClaimStatus.UnderReview, request.Decision == "Approve" ? "Approved" : "Rejected", request.Reason);
    }

    private static async Task<IResult> Transition(int id, User actor, ClaimsDbContext db, ClaimStatus expected, string action, string? notes)
    {
        var next = ClaimTransitions.Next(expected, action) ?? throw new InvalidOperationException("Invalid transition definition.");
        var now = DateTimeOffset.UtcNow;
        // The conditional write is the first operation in the transaction, avoiding a read-then-write race.
        await using var tx = await db.Database.BeginTransactionAsync();
        var eligible = db.Claims.Where(x => x.Id == id && x.Status == expected);
        eligible = action switch
        {
            "Assigned" => eligible.Where(x => x.AssignedOfficerId == null),
            "InformationProvided" => eligible.Where(x => x.ClaimantId == actor.Id),
            _ => eligible.Where(x => x.AssignedOfficerId == actor.Id)
        };
        var count = action == "Assigned"
            ? await eligible.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, next).SetProperty(x => x.AssignedOfficerId, actor.Id).SetProperty(x => x.UpdatedAt, now))
            : await eligible.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, next).SetProperty(x => x.UpdatedAt, now));
        if (count == 0)
        {
            await tx.RollbackAsync();
            var claim = await Visible(db, actor).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (claim is null) return Results.NotFound();
            if (action != "Assigned" && actor.Role == UserRole.ClaimsOfficer && claim.AssignedOfficerId is not null && claim.AssignedOfficerId != actor.Id) return Results.StatusCode(403);
            return Results.Conflict(new { error = "The claim is not in a state that permits this action." });
        }
        if (action == "InformationRequested")
            db.InformationRequests.Add(new InformationRequest { ClaimId = id, RequestedByOfficerId = actor.Id, Message = notes!, RequestedAt = now });
        if (action == "InformationProvided")
        {
            var request = await db.InformationRequests.SingleOrDefaultAsync(x => x.ClaimId == id && x.RespondedAt == null);
            if (request is null) { await tx.RollbackAsync(); return Results.Conflict(new { error = "No outstanding information request." }); }
            request.Response = notes;
            request.RespondedAt = now;
        }
        db.ClaimHistory.Add(new ClaimHistory { ClaimId = id, ActorId = actor.Id, Action = action, Notes = notes, CreatedAt = now });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Ok(ToResponse((await Details(db.Claims).SingleAsync(x => x.Id == id))));
    }
}
