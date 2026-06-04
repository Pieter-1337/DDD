# 05 — Auth Isolation

Auth is the subtlest collision because it isn't fixed by changing ports. Two things must be slot-aware: the **cookies** the Identity host issues, and the **client URLs** it seeds.

## Why ports alone don't isolate auth

Browser cookies are scoped by **host**, not by port (RFC 6265 §8.5). `https://localhost:7010` and `https://localhost:7110` are *the same cookie origin* as far as the browser is concerned. So two IdentityServer instances on different slot ports, open in one browser profile, read and overwrite each other's cookies — logging into slot 2 silently invalidates your slot-1 session.

```
one browser profile, host = localhost
┌─────────────────────────────────────────────┐
│  cookie: .AspNetCore.Identity.Application     │  ← slot 1 and slot 2
│  cookie: idsrv.session                        │     both write the SAME
│  cookie: .AspNetCore.Antiforgery.<hash>       │     names → they stomp
└─────────────────────────────────────────────┘
```

The fix is to make the cookie **names** differ per slot.

## The dev-only cookie-isolation extension

All of the cookie renaming lives in one extension method in the Identity host, `AddWorktreeSlotCookieIsolation()`:

```csharp
public static WebApplicationBuilder AddWorktreeSlotCookieIsolation(this WebApplicationBuilder builder)
{
    var slot = WorktreeSlot.FromValue(builder.Configuration["worktree-slot"]);
    if (slot <= 1)
        return builder;   // slot 1 keeps the framework defaults, byte-for-byte

    var suffix = $".S{slot}";
    builder.Services.ConfigureApplicationCookie(o => o.Cookie.Name = $".AspNetCore.Identity.Application{suffix}");
    builder.Services.ConfigureExternalCookie(o    => o.Cookie.Name = $".AspNetCore.Identity.External{suffix}");
    builder.Services.AddAntiforgery(o             => o.Cookie.Name = $".AspNetCore.Antiforgery.Identity{suffix}");
    builder.Services.Configure<IdentityServerOptions>(o => o.Authentication.CheckSessionCookieName = $"idsrv.session.S{slot}");
    return builder;
}
```

It suffixes **every host-scoped cookie**: the ASP.NET Core Identity application + external cookies, the antiforgery cookie, and Duende's check-session cookie. With distinct names, both slots coexist in one profile:

```
slot 1                              slot 2
.AspNetCore.Identity.Application    .AspNetCore.Identity.Application.S2
idsrv.session                       idsrv.session.S2
.AspNetCore.Antiforgery.<hash>      .AspNetCore.Antiforgery.Identity.S2
```

(The BFF cookie itself — `DDD.Auth` vs `DDD.Auth.S2` — is named from the `Auth:CookieName` config the AppHost injects, so the Scheduling/Billing hosts need no code for it.)

It is called once, dev-gated, from `Program.cs`:

```csharp
if (builder.Environment.IsDevelopment())
    builder.AddWorktreeSlotCookieIsolation();
```

The next doc, [06](06-the-dev-release-split.md), explains why that gate is there and why this code lives in the Identity host rather than the shared building block.

## Slot-aware client seeding

Each slot has its own `IdentityDb_S{N}` and its own Identity process, so it only ever needs *its own* redirect / post-logout / CORS URLs. `IdentityServerConfig.Clients(slot)` derives them from the shared formula:

```csharp
var schedulingPort = WorktreeSlot.Port(WorktreeSlot.SchedulingBasePort, slot);  // 7001, 7101, …
// RedirectUris = { $"https://localhost:{schedulingPort}/signin-oidc" }, etc.
```

The seed runs insert-when-empty into the slot's own config store. So the "register callbacks on create / clean up on teardown" lifecycle falls out of the per-slot database lifecycle for free: a fresh DB is seeded on first boot and dropped on `worktree-destroy` (see [07](07-lifecycle-scripts.md)). No script mutates Duende's tables, and no worktree depends on another's Identity being up.

→ Continue to [06 — The Dev / Release Split](06-the-dev-release-split.md).
