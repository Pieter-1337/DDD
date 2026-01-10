using BuildingBlocks.Application.Interfaces;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries
{
    internal class GetAllPatientsQueryHandler : IRequestHandler<GetAllPatientsQuery, IEnumerable<PatientDto>>
    {
        private readonly IUnitOfWork _uow;

        public GetAllPatientsQueryHandler(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public async Task<IEnumerable<PatientDto>> Handle(GetAllPatientsQuery query, CancellationToken cancellationToken)
        {
            return await _uow.RepositoryFor<Patient>().GetAllAsDtosAsync<PatientDto>(p => p.Status == query.Status);
        }
    }
}
