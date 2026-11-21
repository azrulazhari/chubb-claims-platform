using Chubb.Claims.Api.Data;
using Chubb.Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Api.Workflow;

internal static class DemoIdentity
{
    private const string IdentityKey = "DemoIdentity";

    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() ||
            !int.TryParse(http.Request.Headers["X-Demo-User-Id"], out var id) || id <= 0)
            return Results.Unauthorized();
        var db = http.RequestServices.GetRequiredService<ClaimsDbContext>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (user is null) return Results.Unauthorized();
        http.Items[IdentityKey] = user;
        return await next(context);
    }

    public static User Actor(HttpContext http) => (User)http.Items[IdentityKey]!;
}
