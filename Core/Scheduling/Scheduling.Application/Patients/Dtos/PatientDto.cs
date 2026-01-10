using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Interfaces;
using Scheduling.Domain.Patients;
using System.Linq.Expressions;

namespace Scheduling.Application.Patients.Dtos
{
    public class PatientDto : DtoBase, IEntityDto<Patient, PatientDto>
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? PhoneNumber { get; set; }
        public PatientStatus Status { get; set; }

        public static PatientDto ToDto(Patient patient) => new()
        {
            Id = patient.Id,
            FirstName = patient.FirstName,
            LastName = patient.LastName,
            Email = patient.Email,
            DateOfBirth = patient.DateOfBirth,
            PhoneNumber = patient.PhoneNumber,
            Status = patient.Status,
        };

        public static Expression<Func<Patient, PatientDto>> Project => p => new PatientDto
        {
            Id = p.Id,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = p.PhoneNumber,
            DateOfBirth = p.DateOfBirth,
            Status = p.Status
        };
    }
}
