using BuildingBlocks.Domain;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Domain.Patients;

public class Patient : Entity
{
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public DateTime DateOfBirth { get; private set; }
    public PatientStatus Status { get; private set; }

    private Patient(){}

    // Factory method - the only way to create a Patient
    public static Patient Create(
        string? firstName,
        string? lastName,
        string? email,
        DateTime dateOfBirth,
        string? phoneNumber = null)
    {
        // Validation - enforce invariants
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required", nameof(firstName));

        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required", nameof(lastName));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));

        if (!email.Contains('@'))
            throw new ArgumentException("Invalid email format", nameof(email));

        if (dateOfBirth > DateTime.UtcNow)
            throw new ArgumentException("Date of birth cannot be in the future", nameof(dateOfBirth));

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PhoneNumber = phoneNumber?.Trim(),
            DateOfBirth = dateOfBirth,
            Status = PatientStatus.Active
        };

        // Raise event
        patient.AddDomainEvent(new PatientCreatedEvent(
            patient.Id,
            patient.FirstName,
            patient.LastName,
            patient.Email
        ));

        return patient;
    }

    // Behavior methods - how the entity changes
    public void UpdateContactInfo(string email, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));

        if (!email.Contains('@'))
            throw new ArgumentException("Invalid email format", nameof(email));

        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = phoneNumber?.Trim();
    }

    public void Suspend()
    {
        if (Status == PatientStatus.Suspended)
            return; // Already suspended, no-op

        Status = PatientStatus.Suspended;

        // Raise event
        AddDomainEvent(new PatientSuspendedEvent(Id));
    }

    public void Activate()
    {
        if (Status == PatientStatus.Active)
            return;

        Status = PatientStatus.Active;
    }

    public void Deactivate()
    {
        if (Status == PatientStatus.Inactive)
            return;

        Status = PatientStatus.Inactive;
    }
}
