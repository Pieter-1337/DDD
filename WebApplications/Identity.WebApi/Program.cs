using Identity.WebApi.Data;
using Identity.WebApi.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Worktree slot isolation: cookies are host-scoped (not port), so two IdentityServer
// instances on localhost share cookies in one browser profile unless named per slot.
// Slots 2–5 suffix their host-scoped cookies; slot 1 keeps defaults unchanged.
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

// Suffix the Identity (also Duende's SSO cookie) + antiforgery cookie names per slot.
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

    // Suffix the check-session cookie per slot (see slot note above).
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
