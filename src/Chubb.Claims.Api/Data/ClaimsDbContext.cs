using Chubb.Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Api.Data;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimHistory> ClaimHistory => Set<ClaimHistory>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateClaims();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateClaims();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateClaims()
    {
        foreach (var entry in ChangeTracker.Entries<Claim>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Entity.MarketCode != "MY" || entry.Entity.CurrencyCode != "MYR")
                throw new ArgumentException("Only market MY and currency MYR are supported.");
        }
    }

    private static long ToSen(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amounts cannot be negative.");
        var sen = checked(amount * 100m);
        if (sen != decimal.Truncate(sen))
            throw new ArgumentOutOfRangeException(nameof(amount), "Amounts must be exact to one sen.");
        return checked((long)sen);
    }

    private static decimal ToRinggit(long sen) => sen / 100m;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var users = modelBuilder.Entity<User>();
        users.ToTable(t => {
            t.HasCheckConstraint("CK_Users_DisplayName", "length(trim(\"DisplayName\")) BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_Users_Role", "\"Role\" IN ('Claimant', 'ClaimsOfficer')");
        });
        users.Property(x => x.DisplayName).IsRequired().HasMaxLength(100);
        users.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

        var claims = modelBuilder.Entity<Claim>();
        claims.ToTable(t => {
            t.HasCheckConstraint("CK_Claims_ClaimNumber", "length(trim(\"ClaimNumber\")) BETWEEN 1 AND 40");
            t.HasCheckConstraint("CK_Claims_Description", "length(trim(\"IncidentDescription\")) BETWEEN 1 AND 2000");
            t.HasCheckConstraint("CK_Claims_Amounts", "typeof(\"ClaimedAmountSen\") = 'integer' AND \"ClaimedAmountSen\" > 0 AND typeof(\"EstimatedLiabilitySen\") = 'integer' AND \"EstimatedLiabilitySen\" >= 0");
            t.HasCheckConstraint("CK_Claims_MarketCurrency", "\"MarketCode\" = 'MY' AND \"CurrencyCode\" = 'MYR'");
            t.HasCheckConstraint("CK_Claims_Type", "\"ClaimType\" IN ('Motor', 'Property')");
            t.HasCheckConstraint("CK_Claims_Status", "\"Status\" IN ('Submitted', 'UnderReview', 'InfoRequested', 'Settled', 'Rejected', 'Approved')");
        });
        claims.Property(x => x.MarketCode).IsRequired().HasMaxLength(2);
        claims.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3);
        claims.Property(x => x.ClaimedAmount).HasConversion(value => ToSen(value), value => ToRinggit(value)).HasColumnName("ClaimedAmountSen");
        claims.Property(x => x.EstimatedLiability).HasConversion(value => ToSen(value), value => ToRinggit(value)).HasColumnName("EstimatedLiabilitySen");
        claims.Property(x => x.ClaimNumber).IsRequired().HasMaxLength(40);
        claims.HasIndex(x => x.ClaimNumber).IsUnique();
        claims.Property(x => x.IncidentDescription).IsRequired().HasMaxLength(2000);
        claims.Property(x => x.ClaimType).HasConversion<string>().HasMaxLength(20);
        claims.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        claims.HasOne(x => x.Claimant).WithMany().HasForeignKey(x => x.ClaimantId).OnDelete(DeleteBehavior.Restrict);
        claims.HasOne(x => x.AssignedOfficer).WithMany().HasForeignKey(x => x.AssignedOfficerId).OnDelete(DeleteBehavior.Restrict);

        var history = modelBuilder.Entity<ClaimHistory>();
        history.ToTable(t => {
            t.HasCheckConstraint("CK_History_Action", "length(trim(\"Action\")) BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_History_Notes", "\"Notes\" IS NULL OR length(\"Notes\") <= 2000");
        });
        history.Property(x => x.Action).IsRequired().HasMaxLength(100);
        history.Property(x => x.Notes).HasMaxLength(2000);
        history.HasOne(x => x.Claim).WithMany(x => x.History).HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        history.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);

        var requests = modelBuilder.Entity<InformationRequest>();
        requests.ToTable(t => {
            t.HasCheckConstraint("CK_Requests_Message", "length(trim(\"Message\")) BETWEEN 1 AND 2000");
            t.HasCheckConstraint("CK_Requests_Response", "\"Response\" IS NULL OR length(\"Response\") <= 2000");
        });
        requests.Property(x => x.Message).IsRequired().HasMaxLength(2000);
        requests.Property(x => x.Response).HasMaxLength(2000);
        requests.HasOne(x => x.Claim).WithMany(x => x.InformationRequests).HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        requests.HasOne(x => x.RequestedByOfficer).WithMany().HasForeignKey(x => x.RequestedByOfficerId).OnDelete(DeleteBehavior.Restrict);
    }
}
