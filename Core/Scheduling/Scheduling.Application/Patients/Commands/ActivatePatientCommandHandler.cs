using BuildingBlocks.Application.Interfaces;
using MediatR;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

internal class ActivatePatientCommandHandler : IRequestHandler<ActivatePatientCommand, ActivatePatientCommandResponse>
{
    private readonly IUnitOfWork _uow;

    public ActivatePatientCommandHandler(IUnitOfWork unitOfWork)
    {
        _uow = unitOfWork;
    }

    public async Task<ActivatePatientCommandResponse> Handle(ActivatePatientCommand cmd, CancellationToken cancellationToken)
    {
        var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(cmd.Id, cancellationToken);

        patient!.Activate();

        // Domain event handler (PatientActivatedEventHandler) queues the integration event
        await _uow.SaveChangesAsync(cancellationToken);

        return new ActivatePatientCommandResponse
        {
            Success = true,
            Message = "Patient successfully activated"
        };
    }
}
