using Ardalis.SmartEnum;

namespace BuildingBlocks.Enumerations;

/// <summary>
/// Base class for custom error codes. Automatically prefixes codes with "ERR_".
/// Inherit from this in bounded contexts to define domain-specific errors.
/// </summary>
/// <example>
/// public class PatientErrorCode : ErrorCodeBase&lt;PatientErrorCode&gt;
/// {
///     public static readonly PatientErrorCode AlreadySuspended = new("PATIENT_ALREADY_SUSPENDED", "Patient is already suspended");
///     // Results in code: "ERR_PATIENT_ALREADY_SUSPENDED"
///
///     private PatientErrorCode(string code, string message) : base(code, message) { }
/// }
/// </example>
public abstract class ErrorCodeBase<TEnum> : SmartEnum<TEnum, string>
    where TEnum : SmartEnum<TEnum, string>
{
    private const string Prefix = "ERR_";

    public string Message { get; }

    protected ErrorCodeBase(string code, string message)
        : base(Prefix + code, Prefix + code)
    {
        Message = message;
    }
}

/// <summary>
/// Base class for custom warning codes. Automatically prefixes codes with "WRN_".
/// Inherit from this in bounded contexts to define domain-specific warnings.
/// </summary>
public abstract class WarningCodeBase<TEnum> : SmartEnum<TEnum, string>
    where TEnum : SmartEnum<TEnum, string>
{
    private const string Prefix = "WRN_";

    public string Message { get; }

    protected WarningCodeBase(string code, string message)
        : base(Prefix + code, Prefix + code)
    {
        Message = message;
    }
}

/// <summary>
/// Common error codes shared across all bounded contexts.
/// </summary>
public sealed class ErrorCode : ErrorCodeBase<ErrorCode>
{
    // General errors
    public static readonly ErrorCode NotFound = new("NOT_FOUND", "The requested resource was not found");
    public static readonly ErrorCode InvalidInput = new("INVALID_INPUT", "The provided input is invalid");
    public static readonly ErrorCode Conflict = new("CONFLICT", "The operation conflicts with the current state");
    public static readonly ErrorCode Unauthorized = new("UNAUTHORIZED", "Authentication is required");
    public static readonly ErrorCode Forbidden = new("FORBIDDEN", "You do not have permission to perform this action");
    public static readonly ErrorCode ValidationFailed = new("VALIDATION_FAILED", "One or more validation errors occurred");

    // Field validation errors
    public static readonly ErrorCode FirstNameRequired = new("FIRSTNAME_REQUIRED", "First name is required");
    public static readonly ErrorCode LastNameRequired = new("LASTNAME_REQUIRED", "Last name is required");
    public static readonly ErrorCode EmailRequired = new("EMAIL_REQUIRED", "Email is required");
    public static readonly ErrorCode InvalidEmail = new("INVALID_EMAIL", "Invalid email address");
    public static readonly ErrorCode DateOfBirthRequired = new("DOB_REQUIRED", "Date of birth is required");
    public static readonly ErrorCode InvalidStatus = new("INVALID_STATUS", "Invalid status");

    private ErrorCode(string code, string message) : base(code, message) { }
}
