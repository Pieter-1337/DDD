using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries
{
    public class GetAllPatientsQuery : IRequest<IEnumerable<PatientDto>>
    {
        public PatientStatus Status { get; set; }
    }
}
