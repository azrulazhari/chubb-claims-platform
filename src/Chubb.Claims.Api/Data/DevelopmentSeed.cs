using Chubb.Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Api.Data;

public static class DevelopmentSeed
{
    // Demo identities only: no authentication or authorization is implied.
    public static async Task InitializeAsync(ClaimsDbContext db)
    {
        if (!await db.Users.AnyAsync(x => x.Id == 1))
            db.Users.Add(new User { Id = 1, DisplayName = "Demo Claimant", Role = UserRole.Claimant });
        if (!await db.Users.AnyAsync(x => x.Id == 2))
            db.Users.Add(new User { Id = 2, DisplayName = "Demo Claims Officer", Role = UserRole.ClaimsOfficer });
        await db.SaveChangesAsync();
    }
}
