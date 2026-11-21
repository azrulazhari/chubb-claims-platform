namespace Chubb.Claims.Api.Domain;

public enum UserRole { Claimant, ClaimsOfficer }
public enum ClaimType { Motor, Property }
// Transition rules are intentionally deferred until the claims workflow is defined.
public enum ClaimStatus { Submitted, UnderReview, InfoRequested, Settled, Rejected }

public class User
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}

public class Claim
{
    public int Id { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public int ClaimantId { get; set; }
    public User Claimant { get; set; } = null!;
    public int? AssignedOfficerId { get; set; }
    public User? AssignedOfficer { get; set; }
    public ClaimType ClaimType { get; set; }
    public string IncidentDescription { get; set; } = string.Empty;
    public DateOnly IncidentDate { get; set; }
    public string MarketCode { get; set; } = "MY";
    public string CurrencyCode { get; set; } = "MYR";
    public decimal ClaimedAmount { get; set; }
    public decimal EstimatedLiability { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Submitted;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ClaimHistory> History { get; set; } = new List<ClaimHistory>();
    public ICollection<InformationRequest> InformationRequests { get; set; } = new List<InformationRequest>();
}

public class ClaimHistory
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public Claim Claim { get; set; } = null!;
    public int ActorId { get; set; }
    public User Actor { get; set; } = null!;
    public string Action { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class InformationRequest
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public Claim Claim { get; set; } = null!;
    public int RequestedByOfficerId { get; set; }
    public User RequestedByOfficer { get; set; } = null!;
    public string Message { get; set; } = string.Empty;
    public string? Response { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
}
