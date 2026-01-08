using BuildingBlocks.Application;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands
{
    public record CreatePatientCommand(CreatePatientRequest Patient) : IRequest<CreatePatientCommandResponse>;

    public class CreatePatientRequest
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? PhoneNumber { get; set; }
        public PatientStatus Status { get; set; }
    }

    public class CreatePatientCommandResponse : SuccessOrFailureDto
    {
        public PatientDto PatientDto { get; set; }
    }
}
