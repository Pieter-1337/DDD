using System.Diagnostics;
using System.Globalization;
using BuildingBlocks.Application;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace BuildingBlocks.Tests;

/// <summary>
/// Base class for validator unit tests.
/// Provides DI container with validators and a mocked IUnitOfWork.
/// </summary>
[TestClass]
public abstract class ValidatorTestBase
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
    protected const string VALIDATION_ENUM_VALIDATOR = "EnumValidator";

    #endregion

    private ServiceProvider? _serviceProvider;
    private Stopwatch? _stopwatch;

    protected Mock<IUnitOfWork> UnitOfWorkMock { get; private set; } = null!;

    public TestContext? TestContext { get; set; }

    /// <summary>
    /// Override to register validators and any additional services.
    /// </summary>
    protected abstract void RegisterServices(IServiceCollection services);

    [TestInitialize]
    public virtual void TestInitialize()
    {
        _stopwatch = new Stopwatch();

        var services = new ServiceCollection();

        // Register mocked IUnitOfWork for validators that need it
        UnitOfWorkMock = new Mock<IUnitOfWork>();
        services.AddSingleton(UnitOfWorkMock.Object);

        // Let derived class register validators
        RegisterServices(services);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public virtual void TestCleanup()
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
    }

    #region Service Accessors

    protected IValidator<T> ValidatorFor<T>()
    {
        return _serviceProvider!.GetRequiredService<IValidator<T>>();
    }

    protected T GetService<T>() where T : notnull
    {
        return _serviceProvider!.GetRequiredService<T>();
    }

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
}

/// <summary>
/// Extension methods for FluentValidation assertions.
/// </summary>
public static class ValidationResultExtensions
{
    /// <summary>
    /// Asserts that the validation errors contain an error for the specified property with the specified error code.
    /// </summary>
    public static void ShouldContainValidation(this IList<ValidationFailure> errors, string propertyName, string errorCode)
    {
        var hasError = errors.Any(e => e.PropertyName == propertyName && e.ErrorCode == errorCode);
        if (!hasError)
        {
            var existingErrors = string.Join(", ", errors.Select(e => $"{e.PropertyName}:{e.ErrorCode}"));
            throw new AssertFailedException(
                $"Expected validation error for '{propertyName}' with code '{errorCode}', but found: [{existingErrors}]");
        }
    }

    /// <summary>
    /// Asserts that the validation errors contain an error for the specified property (any error code).
    /// </summary>
    public static void ShouldContainValidationFor(this IList<ValidationFailure> errors, string propertyName)
    {
        var hasError = errors.Any(e => e.PropertyName == propertyName);
        if (!hasError)
        {
            var existingErrors = string.Join(", ", errors.Select(e => e.PropertyName));
            throw new AssertFailedException(
                $"Expected validation error for '{propertyName}', but found: [{existingErrors}]");
        }
    }
}
