using BuildingBlocks.Application.Interfaces;
using MediatR;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

internal class DeletePatientCommandHandler : IRequestHandler<DeletePatientCommand, DeletePatientCommandResponse>
{
    private readonly IUnitOfWork _uow;

    public DeletePatientCommandHandler(IUnitOfWork unitOfWork)
    {
        _uow = unitOfWork;
    }

    public async Task<DeletePatientCommandResponse> Handle(DeletePatientCommand cmd, CancellationToken cancellationToken)
    {
        var patient = await _uow.RepositoryFor<Patient>().GetByIdAsync(cmd.Id, cancellationToken);

        patient!.Delete();

        await _uow.SaveChangesAsync(cancellationToken);

        return new DeletePatientCommandResponse
        {
            Success = true,
            Message = "Patient successfully deleted"
        };
    }
}
