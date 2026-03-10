using Billing.Domain.BillingProfiles;
using Billing.Domain.BillingProfiles.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace Billing.Tests.DomainTests.BillingProfiles;

[TestClass]
public class BillingProfileTests
{
    [TestMethod]
    public void Create_ShouldCreateBillingProfileWithCorrectValues()
    {
        // Arrange
        var patientId = Guid.NewGuid();

        // Act
        var profile = BillingProfile.Create(patientId, "test@example.com", "John Doe");

        // Assert
        profile.Id.ShouldNotBe(Guid.Empty);
        profile.PatientId.ShouldBe(patientId);
        profile.Email.ShouldBe("test@example.com");
        profile.FullName.ShouldBe("John Doe");
        profile.BillingAddress.ShouldBeNull();
        profile.PaymentMethod.ShouldBeNull();
        profile.CreatedAt.ShouldNotBe(default);
    }

    [TestMethod]
    public void Create_ShouldRaiseBillingProfileCreatedEvent()
    {
        // Arrange
        var patientId = Guid.NewGuid();

        // Act
        var profile = BillingProfile.Create(patientId, "test@example.com", "John Doe");

        // Assert
        profile.DomainEvents.Count.ShouldBe(1);
        var domainEvent = profile.DomainEvents[0].ShouldBeOfType<BillingProfileCreatedEvent>();
        domainEvent.BillingProfileId.ShouldBe(profile.Id);
        domainEvent.PatientId.ShouldBe(patientId);
        domainEvent.Email.ShouldBe("test@example.com");
        domainEvent.FullName.ShouldBe("John Doe");
    }

    [TestMethod]
    public void UpdateBillingAddress_ShouldSetAddress()
    {
        // Arrange
        var profile = BillingProfile.Create(Guid.NewGuid(), "test@example.com", "John Doe");

        // Act
        profile.UpdateBillingAddress("123 Main St");

        // Assert
        profile.BillingAddress.ShouldBe("123 Main St");
    }

    [TestMethod]
    public void UpdatePaymentMethod_ShouldSetPaymentMethod()
    {
        // Arrange
        var profile = BillingProfile.Create(Guid.NewGuid(), "test@example.com", "John Doe");
        var paymentMethod = new PaymentMethod("Visa", "4242", "John Doe");

        // Act
        profile.UpdatePaymentMethod(paymentMethod);

        // Assert
        profile.PaymentMethod.ShouldNotBeNull();
        profile.PaymentMethod!.Type.ShouldBe("Visa");
        profile.PaymentMethod.Last4Digits.ShouldBe("4242");
        profile.PaymentMethod.CardholderName.ShouldBe("John Doe");
    }
}
