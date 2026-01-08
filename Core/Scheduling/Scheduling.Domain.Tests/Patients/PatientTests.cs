using Scheduling.Domain.Patients;
using FluentAssertions;
using Xunit;

namespace Scheduling.Domain.Tests.Patients;

public class PatientTests
{
    [Fact]
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
        patient.Id.Should().NotBeEmpty();
        patient.FirstName.Should().Be("John");
        patient.LastName.Should().Be("Doe");
        patient.Email.Should().Be("john.doe@example.com");
        patient.Status.Should().Be(PatientStatus.Active);
    }

    [Fact]
    public void Suspend_ShouldChangeStatusToSuspended()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.Suspend();

        // Assert
        patient.Status.Should().Be(PatientStatus.Suspended);
    }

    [Fact]
    public void Suspend_WhenAlreadySuspended_ShouldRemainSuspended()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
        patient.Suspend();

        // Act
        patient.Suspend(); // Call again

        // Assert
        patient.Status.Should().Be(PatientStatus.Suspended);
    }

    [Fact]
    public void UpdateContactInfo_ShouldUpdateEmail()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "old@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.UpdateContactInfo("new@example.com", null);

        // Assert
        patient.Email.Should().Be("new@example.com");
    }

    [Fact]
    public void Activate_WhenSuspended_ShouldChangeStatusToActive()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
        patient.Suspend();

        // Act
        patient.Activate();

        // Assert
        patient.Status.Should().Be(PatientStatus.Active);
    }
}
