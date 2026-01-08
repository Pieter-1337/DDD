using BuildingBlocks.Application;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Dtos
{
    public class PatientDto : DtoBase
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? PhoneNumber { get; set; }
        public PatientStatus Status { get; set; }
    }
}
