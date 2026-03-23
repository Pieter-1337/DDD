using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Domain.Patients;
using Scheduling.Domain.Patients.Events;
using Shouldly;

namespace Scheduling.Tests.DomainTests.Patients;

[TestClass]
public class PatientTests
{
    [TestMethod]
    public void Create_ShouldCreatePatientWithCorrectValues()
    {
        // Arrange
        var firstName = "John";
        var lastName = "Doe";
        var email = "john.doe@example.com";
        var dateOfBirth = new DateTime(1990, 1, 15);

        // Act
        var patient = Patient.Create(firstName, lastName, email, dateOfBirth);

        // Assert
        patient.Id.ShouldNotBe(Guid.Empty);
        patient.FirstName.ShouldBe("John");
        patient.LastName.ShouldBe("Doe");
        patient.Email.ShouldBe("john.doe@example.com");
        patient.Status.ShouldBe(PatientStatus.Active);
    }

    [TestMethod]
    public void Suspend_ShouldChangeStatusToSuspended()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.Suspend();

        // Assert
        patient.Status.ShouldBe(PatientStatus.Suspended);
    }

    [TestMethod]
    public void Suspend_WhenAlreadySuspended_ShouldRemainSuspended()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
        patient.Suspend();

        // Act
        patient.Suspend(); // Call again

        // Assert
        patient.Status.ShouldBe(PatientStatus.Suspended);
    }

    [TestMethod]
    public void UpdateContactInfo_ShouldUpdateEmail()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "old@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.UpdateContactInfo("new@example.com", null);

        // Assert
        patient.Email.ShouldBe("new@example.com");
    }

    [TestMethod]
    public void Activate_WhenSuspended_ShouldChangeStatusToActive()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
        patient.Suspend();

        // Act
        patient.Activate();

        // Assert
        patient.Status.ShouldBe(PatientStatus.Active);
    }

    [TestMethod]
    public void Delete_ShouldChangeStatusToDeleted()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.Delete();

        // Assert
        patient.Status.ShouldBe(PatientStatus.Deleted);
    }
   
    [TestMethod]
    public void Delete_ShouldRaiseDomainEvent()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.Delete();

        // Assert
        patient.DomainEvents.ShouldContain(e => e is PatientDeletedEvent);
    }
}
