using Scheduling.Domain.Patients;
using FluentAssertions;
using Xunit;

namespace Scheduling.Domain.Tests.Patients;

public class PatientTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreatePatient()
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
    public void Create_WithEmptyFirstName_ShouldThrow()
    {
        // Arrange & Act
        var act = () => Patient.Create("", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("firstName");
    }

    [Fact]
    public void Create_WithInvalidEmail_ShouldThrow()
    {
        // Arrange & Act
        var act = () => Patient.Create("John", "Doe", "invalid-email", DateTime.UtcNow.AddYears(-30));

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("email");
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
    public void UpdateContactInfo_WithValidEmail_ShouldUpdateEmail()
    {
        // Arrange
        var patient = Patient.Create("John", "Doe", "old@example.com", DateTime.UtcNow.AddYears(-30));

        // Act
        patient.UpdateContactInfo("new@example.com", null);

        // Assert
        patient.Email.Should().Be("new@example.com");
    }
}
