using BuildingBlocks.Application;
using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure;
using FizzWare.NBuilder;
using FluentValidation;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application;
using Scheduling.Infrastructure.Persistence;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Scheduling.Tests.Common;

/// <summary>
/// Base class for integration tests.
/// Each test runs within a transaction that is rolled back after the test completes,
/// ensuring test isolation without recreating the database.
/// </summary>
[TestClass]
public abstract class TestBase
{
    #region FluentValidation Error Codes
    protected const string VALIDATION_NULL_VALIDATOR = "NullValidator";
    protected const string VALIDATION_EMPTY_VALIDATOR = "EmptyValidator";
    protected const string VALIDATION_NOT_EMPTY_VALIDATOR = "NotEmptyValidator";
    protected const string VALIDATION_NOT_NULL_VALIDATOR = "NotNullValidator";
    protected const string VALIDATION_GREATERTHAN_VALIDATOR = "GreaterThanValidator";
    protected const string VALIDATION_GREATERTHANOREQUAL_VALIDATOR = "GreaterThanOrEqualValidator";
    protected const string VALIDATION_LESSTHAN_VALIDATOR = "LessThanValidator";
    protected const string VALIDATION_LESSTHANOREQUAL_VALIDATOR = "LessThanOrEqualValidator";
    protected const string VALIDATION_INCLUSIVEBETWEEN_VALIDATOR = "InclusiveBetweenValidator";
    protected const string VALIDATION_MAXLENGTH_VALIDATOR = "MaximumLengthValidator";
    protected const string VALIDATION_PREDICATE_VALIDATOR = "PredicateValidator";
    protected const string VALIDATION_ASYNCPREDICATE_VALIDATOR = "AsyncPredicateValidator";
    protected const string VALIDATION_EMAIL_VALIDATOR = "EmailValidator";
    protected const string VALIDATION_EQUAL_VALIDATOR = "EqualValidator";
    protected const string VALIDATION_NOT_EQUAL_VALIDATOR = "NotEqualValidator";
    protected const string VALIDATION_REGEX_VALIDATOR = "RegularExpressionValidator";
    #endregion

    private ServiceProvider? _serviceProvider;
    private SqliteConnection? _connection;
    private Stopwatch? _stopwatch;
    private IServiceScope? _scope;

    public TestContext? TestContext { get; set; }

    static TestBase()
    {
        ConfigureNBuilder();
    }

    [TestInitialize]
    public void TestInitialize()
    {
        _stopwatch = new Stopwatch();

        // SQLite in-memory requires the connection to stay open
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        // Register logging (required by event handlers)
        services.AddLogging();

        // Register DbContext with SQLite
        services.AddDbContext<SchedulingDbContext>(options =>
            options.UseSqlite(_connection));

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork, UnitOfWork<SchedulingDbContext>>();

        // Register MediatR and handlers
        services.AddSchedulingApplication();

        // Register validators (when they exist)
        services.AddValidatorsFromAssembly(typeof(Scheduling.Application.ServiceCollectionExtensions).Assembly);

        _serviceProvider = services.BuildServiceProvider();

        // Ensure database is created
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        dbContext.Database.EnsureCreated();

        // Create scope for test and begin transaction
        _scope = _serviceProvider.CreateScope();
        Uow.BeginTransactionAsync().GetAwaiter().GetResult();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        // Rollback transaction to keep database clean
        Uow.CloseTransactionAsync(new Exception("Test rollback")).GetAwaiter().GetResult();

        _scope?.Dispose();
        _scope = null;

        _serviceProvider?.Dispose();
        _serviceProvider = null;

        _connection?.Dispose();
        _connection = null;
    }

    #region Service Accessors

    protected IMediator GetMediator()
    {
        return _scope!.ServiceProvider.GetRequiredService<IMediator>();
    }

    protected IValidator<T> ValidatorFor<T>()
    {
        return _scope!.ServiceProvider.GetRequiredService<IValidator<T>>();
    }

    protected IUnitOfWork Uow => _scope!.ServiceProvider.GetRequiredService<IUnitOfWork>();

    protected SchedulingDbContext DbContext => _scope!.ServiceProvider.GetRequiredService<SchedulingDbContext>();

    #endregion

    #region Stopwatch

    protected void StartStopwatch() => _stopwatch?.Restart();

    protected void StopStopwatch() => _stopwatch?.Stop();

    protected decimal ElapsedSeconds() => _stopwatch is not null
        ? (decimal)_stopwatch.Elapsed.TotalSeconds
        : -1;

    protected long ElapsedMilliseconds() => _stopwatch?.ElapsedMilliseconds ?? -1;

    #endregion

    #region Culture

    protected void SetCurrentCulture(string language = "en-GB")
    {
        CultureInfo.CurrentCulture = new CultureInfo(language);
        CultureInfo.CurrentUICulture = new CultureInfo(language);
    }

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
