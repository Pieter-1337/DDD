using BuildingBlocks.Domain;

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
        string? phoneNumber = null)
    {
        return new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PhoneNumber = phoneNumber?.Trim(),
            DateOfBirth = dateOfBirth,
            Status = PatientStatus.Active
        };
    }

    public void UpdateContactInfo(string email, string phoneNumber)
    {
        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = phoneNumber?.Trim();
    }

    public void Suspend()
    {
        if (Status == PatientStatus.Suspended)
            return;

        Status = PatientStatus.Suspended;
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
