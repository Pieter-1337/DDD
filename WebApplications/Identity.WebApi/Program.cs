using Identity.WebApi.Data;
using Identity.WebApi.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Worktree slot isolation (slots 2–5): two IdentityServer instances run on the same host
// (localhost), and cookies are scoped by host, not port — so without distinct names their
// login / check-session / antiforgery cookies overwrite each other in a single browser
// profile, and logging out / re-challenging one slot disturbs the other. Suffix those
// host-scoped cookie names per slot so the slots stay independent. Slot 1 (main checkout)
// keeps the bare default names — behaviour is byte-for-byte unchanged. Mirrors the slot
// read in IdentitySeedData; the AppHost injects 'worktree-slot' for slots 2–5 only.
var slot = int.TryParse(builder.Configuration["worktree-slot"], out var slotValue) ? slotValue : 1;

// Add services to the container.
builder.Services.AddRazorPages();

// Database connection
var connectionString = builder.Configuration.GetConnectionString("IdentityDb");

var migrationsAssembly = typeof(Program).Assembly.GetName().Name;

// ASP.NET Core Identity
builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors();
        options.EnableSensitiveDataLogging();
    }
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password settings (relax for development, tighten for production)
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 6;
    options.Password.RequiredUniqueChars = 1;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false; // Disable for dev
})
.AddEntityFrameworkStores<IdentityDbContext>()
.AddDefaultTokenProviders();

// Slot-suffix the host-scoped ASP.NET Core Identity + antiforgery cookies for slots 2–5
// (see the slot note above). The Identity.Application cookie is also Duende's SSO login
// cookie (via AddAspNetIdentity), so this is what keeps two logged-in slots independent.
// Gated to slot > 1 so slot 1 keeps the framework defaults untouched.
if (slot > 1)
{
    var cookieSuffix = $".S{slot}";
    builder.Services.ConfigureApplicationCookie(options => options.Cookie.Name = $".AspNetCore.Identity.Application{cookieSuffix}");
    builder.Services.ConfigureExternalCookie(options => options.Cookie.Name = $".AspNetCore.Identity.External{cookieSuffix}");
    builder.Services.AddAntiforgery(options => options.Cookie.Name = $".AspNetCore.Antiforgery.Identity{cookieSuffix}");
}

// Data Protection (for cookie encryption across multiple instances)
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<IdentityDbContext>()
    .SetApplicationName("DDD.Identity");

// Duende IdentityServer
builder.Services.AddIdentityServer(options =>
{
    options.EmitStaticAudienceClaim = true; // Include 'aud' claim in tokens

    options.Events.RaiseErrorEvents = true;
    options.Events.RaiseInformationEvents = true;
    options.Events.RaiseFailureEvents = true;
    options.Events.RaiseSuccessEvents = true;

    // Slot-suffix Duende's check-session cookie so concurrent slots don't share it on
    // localhost (see the slot note above). Slot 1 keeps the default 'idsrv.session'.
    if (slot > 1)
    {
        options.Authentication.CheckSessionCookieName = $"idsrv.session.S{slot}";
    }
})
    // Integrate with ASP.NET Core Identity
    .AddAspNetIdentity<ApplicationUser>()

    // Configuration store (clients, resources, scopes)
    .AddConfigurationStore(options =>
    {
        options.ConfigureDbContext = b =>
            b.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
    })

    // Operational store (tokens, consents, codes)
    .AddOperationalStore(options =>
    {
        options.ConfigureDbContext = b =>
            b.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));

        // Enable automatic token cleanup
        options.EnableTokenCleanup = true;
        options.TokenCleanupInterval = 3600; // seconds (1 hour)
    });
// Note: Duende v7+ has automatic key management built in � no need for AddDevelopmentSigningCredential().
// Keys are generated, rotated, and stored automatically.

// Add hosted service for seeding data
builder.Services.AddHostedService<IdentitySeedData>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Authentication & Authorization
app.UseAuthentication();
app.UseIdentityServer(); // Duende IdentityServer middleware
app.UseAuthorization();

app.MapRazorPages();

app.Run();
