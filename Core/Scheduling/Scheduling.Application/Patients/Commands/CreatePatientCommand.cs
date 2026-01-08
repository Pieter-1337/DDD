using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands
{
    public record CreatePatientCommand(PatientDto Patient) : IRequest<Patient>;
}
