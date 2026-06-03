using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Applies any pending EF Core migrations for the Billing bounded context on startup.
/// Runs only in the Development environment — dev/staging/prod migrate via the deployment
/// pipeline (e.g. <c>dotnet ef database update</c> or migration bundles).
/// This mirrors the pattern in <c>Identity.WebApi/SeedData/IdentitySeedData.cs</c>.
/// </summary>
public class DatabaseMigrator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _environment;

    public DatabaseMigrator(IServiceProvider serviceProvider, IHostEnvironment environment)
    {
        _serviceProvider = serviceProvider;
        _environment = environment;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return;

        using var scope = _serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
