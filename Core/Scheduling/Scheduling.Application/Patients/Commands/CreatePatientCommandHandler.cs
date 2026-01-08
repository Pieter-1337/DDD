using BuildingBlocks.Domain.Interfaces;
using MediatR;
using Scheduling.Application.Patients.Events;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands
{
    public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Patient>
    {
        private readonly IUnitOfWork _uow;
        private readonly IMediator _mediator;
        public CreatePatientCommandHandler(IUnitOfWork unitOfWork, IMediator mediator)
        {
            _uow = unitOfWork;
            _mediator = mediator;
        }

        public async Task<Patient> Handle(CreatePatientCommand cmd, CancellationToken cancellationToken)
        {
            var dto = cmd.Patient;
            var patient = Patient.Create(dto.FirstName, dto.LastName, dto.Email, dto.DateOfBirth, dto.PhoneNumber);

            _uow.RepositoryFor<Patient>().Add(patient);

            await _uow.SaveChangesAsync(cancellationToken);
            await _mediator.Publish(new PatientCreatedEvent(patient.Id, patient.FirstName!, patient.LastName!, patient.Email!), cancellationToken);

            return patient;
        }
    }
}
