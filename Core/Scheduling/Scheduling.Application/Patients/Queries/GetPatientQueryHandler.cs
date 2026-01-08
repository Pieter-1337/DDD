using BuildingBlocks.Application;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

internal class GetPatientQueryHandler : IRequestHandler<GetPatientQuery, PatientDto?>
{
    private readonly IUnitOfWork _uow;

    public GetPatientQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<PatientDto?> Handle(GetPatientQuery query, CancellationToken cancellationToken)
    {
        return await _uow.RepositoryFor<Patient>()
            .FirstOrDefaultAsDtoAsync<PatientDto>(p => p.Id == query.Id, cancellationToken);
    }
}
