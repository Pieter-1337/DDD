using BuildingBlocks.Enumerations;

namespace Scheduling.Domain.Patients;

public sealed class PatientStatus : SmartEnumBase<PatientStatus>
{
    public static readonly PatientStatus Active = new(nameof(Active), 1);
    public static readonly PatientStatus Inactive = new(nameof(Inactive), 2);
    public static readonly PatientStatus Suspended = new(nameof(Suspended), 3);

    private PatientStatus(string name, int value) : base(name, value) { }
}
