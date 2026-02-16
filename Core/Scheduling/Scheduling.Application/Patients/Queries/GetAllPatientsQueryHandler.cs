using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Domain.Specifications;
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
            // Start with a base that matches everything
            var predicate = PredicateBuilder.BaseAnd<Patient>();

            // Conditionally add filters
            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var status = PatientStatus.FromName(query.Status);
                predicate = predicate.And(p => p.Status == status);
            }

            // If no filters were added, predicate is still valid (matches all)
            return await _uow.RepositoryFor<Patient>()
                .GetAllAsDtosAsync<PatientDto>(predicate, cancellationToken);
        }
    }
}
