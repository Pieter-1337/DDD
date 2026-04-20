using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Identity.WebApi.Config;
using Identity.WebApi.Data;
using Identity.WebApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Seeds the identity database with test users, roles, and OAuth clients.
/// Runs once on application startup in development environment.
/// </summary>
public class IdentitySeedData : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _environment;

    public IdentitySeedData(IServiceProvider serviceProvider, IHostEnvironment environment)
    {
        _serviceProvider = serviceProvider;
        _environment = environment;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Only seed in development
        if (!_environment.IsDevelopment())
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();

        // Ensure databases are created
        var identityContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identityContext.Database.EnsureCreatedAsync(cancellationToken);

        var configurationContext = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        await configurationContext.Database.EnsureCreatedAsync(cancellationToken);

        var persistedGrantContext = scope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>();
        await persistedGrantContext.Database.EnsureCreatedAsync(cancellationToken);

        await SeedRolesAsync(scope.ServiceProvider);
        await SeedUsersAsync(scope.ServiceProvider);
        await SeedIdentityServerConfigurationAsync(scope.ServiceProvider);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedRolesAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        string[] roles = { "Admin", "User", "Doctor", "Nurse" };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task SeedUsersAsync(IServiceProvider serviceProvider)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Admin user
        var adminEmail = "admin@test.com";
        if (await userManager.FindByEmailAsync(adminEmail) == null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(admin, "Admin123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }

        // Regular user
        var userEmail = "user@test.com";
        if (await userManager.FindByEmailAsync(userEmail) == null)
        {
            var user = new ApplicationUser
            {
                UserName = userEmail,
                Email = userEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, "User123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, "User");
            }
        }

        // Doctor user
        var doctorEmail = "doctor@test.com";
        if (await userManager.FindByEmailAsync(doctorEmail) == null)
        {
            var doctor = new ApplicationUser
            {
                UserName = doctorEmail,
                Email = doctorEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(doctor, "Doctor123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(doctor, "Doctor");
            }
        }
    }

    private static async Task SeedIdentityServerConfigurationAsync(IServiceProvider serviceProvider)
    {
        var context = serviceProvider.GetRequiredService<ConfigurationDbContext>();

        // Seed Identity Resources
        if (!await context.IdentityResources.AnyAsync())
        {
            foreach (var resource in IdentityServerConfig.IdentityResources)
            {
                context.IdentityResources.Add(resource.ToEntity());
            }
            await context.SaveChangesAsync();
        }

        // Seed API Scopes
        if (!await context.ApiScopes.AnyAsync())
        {
            foreach (var scope in IdentityServerConfig.ApiScopes)
            {
                context.ApiScopes.Add(scope.ToEntity());
            }
            await context.SaveChangesAsync();
        }

        // Seed Clients
        if (!await context.Clients.AnyAsync())
        {
            foreach (var client in IdentityServerConfig.Clients)
            {
                context.Clients.Add(client.ToEntity());
            }
            await context.SaveChangesAsync();
        }
    }
}