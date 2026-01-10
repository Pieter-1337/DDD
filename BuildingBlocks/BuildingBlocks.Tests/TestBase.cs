using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using FluentValidation;
using FizzWare.NBuilder;
using BuildingBlocks.Application.Interfaces;

namespace BuildingBlocks.Tests;

/// <summary>
/// Base class for integration tests.
/// Each test runs within a transaction that is rolled back after the test completes,
/// ensuring test isolation without recreating the database.
/// </summary>
[TestClass]
public abstract class TestBase<TContext> : ValidatorTestBase where TContext : DbContext
{
    private ServiceProvider? _serviceProvider;
    private SqliteConnection? _connection;
    private IServiceScope? _scope;

    static TestBase()
    {
        ConfigureNBuilder();
    }

    /// <summary>
    /// Override to register bounded context-specific services (MediatR, validators, etc.)
    /// </summary>
    protected abstract void RegisterBoundedContextServices(IServiceCollection services);

    protected sealed override void RegisterServices(IServiceCollection services)
    {
        // This is called by ValidatorTestBase, but we override the full initialization
        // So this won't be used - we register everything in TestInitialize
    }

    [TestInitialize]
    public override void TestInitialize()
    {
        // Don't call base - we handle everything here with real database
        base.TestInitialize(); // For stopwatch only

        // SQLite in-memory requires the connection to stay open
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        // Register logging (required by event handlers)
        services.AddLogging();

        // Register DbContext with SQLite
        services.AddDbContext<TContext>(options =>
            options.UseSqlite(_connection));

        // Register UnitOfWork (real implementation, not mock)
        services.AddScoped<IUnitOfWork, UnitOfWork<TContext>>();

        // Register bounded context services (MediatR, validators, etc.)
        RegisterBoundedContextServices(services);

        _serviceProvider = services.BuildServiceProvider();

        // Ensure database is created
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
        dbContext.Database.EnsureCreated();

        // Create scope for test and begin transaction
        _scope = _serviceProvider.CreateScope();
        Uow.BeginTransactionAsync().GetAwaiter().GetResult();
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        // Rollback transaction to keep database clean
        Uow.CloseTransactionAsync(new Exception("Test rollback")).GetAwaiter().GetResult();

        _scope?.Dispose();
        _scope = null;

        _serviceProvider?.Dispose();
        _serviceProvider = null;

        _connection?.Dispose();
        _connection = null;

        base.TestCleanup();
    }

    #region Service Accessors

    protected IMediator GetMediator()
    {
        return _scope!.ServiceProvider.GetRequiredService<IMediator>();
    }

    protected new IValidator<T> ValidatorFor<T>()
    {
        return _scope!.ServiceProvider.GetRequiredService<IValidator<T>>();
    }

    protected IUnitOfWork Uow => _scope!.ServiceProvider.GetRequiredService<IUnitOfWork>();

    protected TContext DbContext => _scope!.ServiceProvider.GetRequiredService<TContext>();

    #endregion

    #region Validation Helpers

    /// <summary>
    /// Returns true if the errorMessage matches the validationMessage with arguments matching pattern
    /// </summary>
    protected bool RegexMatchValidation(string errorMessage, string validationMessage)
    {
        string patternForArguments = Regex.Escape("{") + @"[\w.,:\/ ]+}";
        string valMessagePattern = Regex.Replace(validationMessage, patternForArguments, @"[\w.,:\/ ]+");
        return Regex.IsMatch(errorMessage, valMessagePattern.Replace("(", "\\(").Replace(")", "\\)"));
    }

    #endregion

    #region NBuilder Configuration

    private static void ConfigureNBuilder()
    {
        // Disable auto-naming for base entity properties
        BuilderSetup.DisablePropertyNamingFor<Entity, Guid>(x => x.Id);
    }

    #endregion
}
