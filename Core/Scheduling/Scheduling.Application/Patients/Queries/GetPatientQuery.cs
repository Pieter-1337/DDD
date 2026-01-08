using MediatR;
using Scheduling.Application.Patients.Dtos;

namespace Scheduling.Application.Patients.Queries;

public class GetPatientQuery : IRequest<PatientDto?>
{
    public Guid Id { get; set; }
}
