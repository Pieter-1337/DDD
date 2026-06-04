using BuildingBlocks.WorktreeSlots;
using Duende.IdentityServer.Configuration;

namespace Identity.WebApi;

/// <summary>
/// Dev-only worktree-slot isolation for the Identity host. Cookies are host-scoped
/// (localhost), not port-scoped, so two IdentityServer instances on different slot
/// ports would share cookies in one browser profile. This suffixes every host-scoped
/// cookie with <c>.S{slot}</c> so slots stay independent in a single profile.
///
/// The cookie wiring lives here (not in a shared BuildingBlock) because it touches
/// Duende's <see cref="IdentityServerOptions"/> and the ASP.NET Core framework cookies
/// — both Identity-host concerns. The shared <see cref="WorktreeSlot"/> supplies only
/// the slot number. Slot 1 (and any non-dev environment) is a no-op, so the deployed
/// release path keeps framework defaults byte-for-byte.
/// </summary>
public static class WorktreeSlotCookieIsolation
{
    public static WebApplicationBuilder AddWorktreeSlotCookieIsolation(this WebApplicationBuilder builder)
    {
        var slot = WorktreeSlot.FromValue(builder.Configuration["worktree-slot"]);
        if (slot <= 1)
            return builder; // slot 1 keeps the framework default cookie names unchanged

        var suffix = $".S{slot}";

        builder.Services.ConfigureApplicationCookie(o => o.Cookie.Name = $".AspNetCore.Identity.Application{suffix}");
        builder.Services.ConfigureExternalCookie(o => o.Cookie.Name = $".AspNetCore.Identity.External{suffix}");
        builder.Services.AddAntiforgery(o => o.Cookie.Name = $".AspNetCore.Antiforgery.Identity{suffix}");
        builder.Services.Configure<IdentityServerOptions>(o => o.Authentication.CheckSessionCookieName = $"idsrv.session.S{slot}");

        return builder;
    }
}
