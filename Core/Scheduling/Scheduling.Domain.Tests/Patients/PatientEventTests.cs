using FluentAssertions;
using Scheduling.Domain.Patients;
using Scheduling.Domain.Patients.Events;
using Xunit;

namespace Scheduling.Domain.Tests.Patients
{
    public class PatientEventTests
    {
        [Fact]
        public void Create_ShouldRaisePatientCreatedEvent()
        {
            // Arrange & Act
            var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));

            // Assert
            patient.DomainEvents.Should().ContainSingle();
            patient.DomainEvents.First().Should().BeOfType<PatientCreatedEvent>();

            var @event = (PatientCreatedEvent)patient.DomainEvents.First();
            @event.PatientId.Should().Be(patient.Id);
            @event.Email.Should().Be("test@example.com");
        }

        [Fact]
        public void Suspend_ShouldRaisePatientSuspendedEvent()
        {
            // Arrange
            var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
            patient.ClearDomainEvents(); // Clear the Created event

            // Act
            patient.Suspend();

            // Assert
            patient.DomainEvents.Should().ContainSingle();
            patient.DomainEvents.First().Should().BeOfType<PatientSuspendedEvent>();
        }

        [Fact]
        public void Suspend_WhenAlreadySuspended_ShouldNotRaiseEvent()
        {
            // Arrange
            var patient = Patient.Create("John", "Doe", "test@example.com", DateTime.UtcNow.AddYears(-30));
            patient.Suspend();
            patient.ClearDomainEvents();

            // Act
            patient.Suspend(); // Second time

            // Assert
            patient.DomainEvents.Should().BeEmpty(); // No new event
        }
    }
}
