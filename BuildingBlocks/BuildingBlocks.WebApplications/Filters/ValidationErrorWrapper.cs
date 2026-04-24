using BuildingBlocks.Enumerations;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using System.Text.Json.Serialization;

namespace BuildingBlocks.WebApplications.Filters
{
    public class ValidationErrorWrapper
    {
        public List<ValidationItem> Errors { get; init; } = [];
        public List<ValidationItem> Warnings { get; init; } = [];

        [JsonIgnore]
        public int HttpStatusCode { get; private set; } = StatusCodes.Status400BadRequest;

        public ValidationErrorWrapper(ValidationException exception)
        {
            ProcessValidationFailures(exception);
        }

        private void ProcessValidationFailures(ValidationException exception)
        {
            // Separate by severity - warnings first, then errors
            var warnings = exception.Errors
                .Where(e => e.Severity == Severity.Warning)
                .ToList();

            var errors = exception.Errors
                .Where(e => e.Severity == Severity.Error)
                .ToList();

            // Process warnings
            foreach (var warning in warnings)
            {
                Warnings.Add(new ValidationItem
                {
                    Code = ExtractCustomCode(warning.ErrorCode, "WRN_"),
                    Message = warning.ErrorMessage
                });
            }

            // Process errors
            foreach (var error in errors)
            {
                Errors.Add(new ValidationItem
                {
                    Code = ExtractCustomCode(error.ErrorCode, "ERR_"),
                    Message = error.ErrorMessage
                });
            }

            // Allow single validation failure to override HTTP status code
            TrySetCustomHttpStatusCode(exception);
        }

        private static string? ExtractCustomCode(string? errorCode, string prefix)
        {
            // Only include codes that start with the expected prefix
            if (string.IsNullOrEmpty(errorCode))
                return null;

            return errorCode.StartsWith(prefix) ? errorCode : null;
        }

        private void TrySetCustomHttpStatusCode(ValidationException exception)
        {
            // Forbidden takes precedence: if any error is the role-gate failure from
            // UserValidator<T>, return 403 regardless of what else failed. Response body
            // still uses the standard ValidationErrorWrapper shape — only status differs.
            if (exception.Errors.Any(e => e.ErrorCode == ErrorCode.Forbidden.Value))
            {
                HttpStatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            // If single error with numeric code between 100-599, use as HTTP status
            if (exception.Errors.Count() != 1)
                return;

            var singleError = exception.Errors.Single();

            if (int.TryParse(singleError.ErrorCode, out var httpCode) &&
                httpCode >= 100 && httpCode < 600)
            {
                HttpStatusCode = httpCode;
            }
        }

        public class ValidationItem
        {
            public string? Code { get; init; }
            public required string Message { get; init; }
        }
    }
}
