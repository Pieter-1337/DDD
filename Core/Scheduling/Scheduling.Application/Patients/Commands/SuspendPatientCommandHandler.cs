using BuildingBlocks.Application.Interfaces;
using MediatR;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

internal class SuspendPatientCommandHandler : IRequestHandler<SuspendPatientCommand, SuspendPatientCommandResponse>
{
    private readonly IUnitOfWork _uow;

    public SuspendPatientCommandHandler(IUnitOfWork unitOfWork)
    {
        _uow = unitOfWork;
    }

    public async Task<SuspendPatientCommandResponse> Handle(SuspendPatientCommand cmd, CancellationToken cancellationToken)
    {
        var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(cmd.Id, cancellationToken);

        patient!.Suspend();
        await _uow.SaveChangesAsync(cancellationToken);

        return new SuspendPatientCommandResponse
        {
            Success = true,
            Message = "Patient successfully suspended"
        };
    }
}
