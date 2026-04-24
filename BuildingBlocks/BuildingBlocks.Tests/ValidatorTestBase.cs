using BuildingBlocks.Application.Auth;
using BuildingBlocks.Application.Interfaces;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using System.Diagnostics;
using System.Globalization;

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

    protected Mock<ICurrentUser> CurrentUserMock { get; private set; } = null!;

    [TestInitialize]
    public virtual void TestInitialize()
    {
        _stopwatch = new Stopwatch();

        var services = new ServiceCollection();

        // Register mocked IUnitOfWork for validators that need it
        UnitOfWorkMock = new Mock<IUnitOfWork>();
        services.AddSingleton(UnitOfWorkMock.Object);

        // NEW: mocked ICurrentUser — authenticated but with no roles by default.
        // Tests that exercise a role-gated validator call SetupUserRoles(...) in
        // their Arrange block to give the caller an allowed role.
        CurrentUserMock = new Mock<ICurrentUser>();
        CurrentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        CurrentUserMock.Setup(u => u.UserId).Returns("test-user-id");
        CurrentUserMock.Setup(u => u.Name).Returns("Test User");
        CurrentUserMock.Setup(u => u.Email).Returns("test@test.com");
        CurrentUserMock.Setup(u => u.Roles).Returns(Array.Empty<string>());
        CurrentUserMock.Setup(u => u.HasRole(It.IsAny<string>())).Returns(false);
        services.AddSingleton(CurrentUserMock.Object);

        // Let derived class register validators
        RegisterServices(services);

        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Configure the mocked ICurrentUser to return the given roles.
    /// Call this in the Arrange section of any test that exercises a role-gated validator.
    /// </summary>
    protected void SetupUserRoles(params string[] roles)
    {
        var list = roles.ToList();
        CurrentUserMock.Setup(u => u.Roles).Returns(list);
        CurrentUserMock.Setup(u => u.HasRole(It.IsAny<string>()))
            .Returns((string r) => list.Contains(r));
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
    /// Assert if a validation message exists with specified property name and error code
    /// </summary>
    /// <param name="actual"></param>
    /// <param name="propertyName">Property name (translated)</param>
    /// <param name="errorCode">Validation rule name</param>
    public static void ShouldContainValidation(this IEnumerable<ValidationFailure> actual, string propertyName, string errorCode)
    {
        actual.ShouldContain(e => e.FormattedMessagePlaceholderValues.Contains(new KeyValuePair<string, object>("PropertyName", propertyName)) && e.ErrorCode == errorCode,
            $"No validation message exists with property name '{propertyName}' for rule '{errorCode}'");
    }
}
