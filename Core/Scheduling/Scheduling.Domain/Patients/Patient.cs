using BuildingBlocks.Domain;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Domain.Patients;

public class Patient : Entity
{
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public DateTime DateOfBirth { get; private set; }
    public PatientStatus Status { get; private set; }

    private Patient() { }

    public static Patient Create(
        string firstName,
        string lastName,
        string email,
        DateTime dateOfBirth,
        string? phoneNumber = null,
        PatientStatus? status = null)
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PhoneNumber = phoneNumber?.Trim(),
            DateOfBirth = dateOfBirth,
            Status = status ?? PatientStatus.Active
        };

        patient.AddDomainEvent(new PatientCreatedEvent(
            patient.Id,
            patient.FirstName,
            patient.LastName,
            patient.Email,
            patient.DateOfBirth));

        return patient;
    }

    public void UpdateContactInfo(string email, string? phoneNumber)
    {
        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = phoneNumber?.Trim();
    }

    public void Suspend(string reason = "")
    {
        if (Status == PatientStatus.Suspended)
            return;

        Status = PatientStatus.Suspended;

        AddDomainEvent(new PatientSuspendedEvent(Id, reason));
    }

    public void Activate()
    {
        if (Status == PatientStatus.Active)
            return;

        Status = PatientStatus.Active;

        AddDomainEvent(new PatientActivatedEvent(Id));
    }

    public void Delete()
    {
        if (Status == PatientStatus.Deleted)
            return;

        Status = PatientStatus.Deleted;

        AddDomainEvent(new PatientDeletedEvent(Id));
    }
}
