namespace Billing.Domain.BillingProfiles;

/// <summary>
/// Value object representing a patient's payment method.
/// Immutable record type following DDD value object pattern.
/// </summary>
public record PaymentMethod(
    string Type,
    string Last4Digits,
    string? CardholderName = null);
