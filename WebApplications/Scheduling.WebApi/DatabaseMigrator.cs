using Microsoft.EntityFrameworkCore;
using Scheduling.Infrastructure.Persistence;

/// <summary>
/// Applies any pending EF Core migrations for the Scheduling bounded context on startup.
/// Runs only in the Development environment — dev/staging/prod migrate via the deployment
/// pipeline (e.g. <c>dotnet ef database update</c> or migration bundles).
/// This mirrors the pattern in <c>Identity.WebApi/SeedData/IdentitySeedData.cs</c>.
/// </summary>
public class DatabaseMigrator : IHostedService
{
    // Bound the migrate so an unreachable database fails fast with a clear reason instead
    // of hanging host startup (Kestrel never binds, and requests stall behind the Aspire
    // proxy with no upstream). 60s covers a genuine first-run migration; a wedged
    // connection trips the timeout well before that.
    private static readonly TimeSpan MigrationTimeout = TimeSpan.FromSeconds(60);

    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(IServiceProvider serviceProvider, IHostEnvironment environment, ILogger<DatabaseMigrator> logger)
    {
        _serviceProvider = serviceProvider;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return;

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(MigrationTimeout);

        try
        {
            _logger.LogInformation("Applying pending EF Core migrations for {Context}…", nameof(SchedulingDbContext));
            await context.Database.MigrateAsync(timeoutCts.Token);
            _logger.LogInformation("{Context} schema is up to date.", nameof(SchedulingDbContext));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // host is shutting down — not a migration failure
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Database migration timed out after {MigrationTimeout.TotalSeconds:N0}s. SQL Server at the " +
                "'DefaultConnection' connection string is likely unreachable. Check that SQL Server/LocalDB is " +
                "running and that ConnectionStrings:DefaultConnection in user-secrets is correct.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply migrations for {Context}.", nameof(SchedulingDbContext));
            throw new InvalidOperationException(
                "Database migration failed for the Scheduling context. Verify SQL Server/LocalDB is running and that " +
                "ConnectionStrings:DefaultConnection in user-secrets is correct, then apply migrations with " +
                "'dotnet ef database update --project Core/Scheduling/Scheduling.Infrastructure --startup-project WebApplications/Scheduling.WebApi'.",
                ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
