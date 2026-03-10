using BuildingBlocks.Application.Interfaces;
using MediatR;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands;

internal class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, CreatePatientCommandResponse>
{
    private readonly IUnitOfWork _uow;

    public CreatePatientCommandHandler(IUnitOfWork unitOfWork)
    {
        _uow = unitOfWork;
    }

    public async Task<CreatePatientCommandResponse> Handle(CreatePatientCommand cmd, CancellationToken cancellationToken)
    {
        var request = cmd.Patient;
        var status = PatientStatus.FromName(request.Status);
        var patient = Patient.Create(request.FirstName, request.LastName, request.Email, request.DateOfBirth, request.PhoneNumber, status);
        _uow.RepositoryFor<Patient>().Add(patient);

        // Domain event handler (PatientCreatedEventHandler) queues the integration event
        await _uow.SaveChangesAsync(cancellationToken);

        return new CreatePatientCommandResponse
        {
            Success = true,
            Message = "Patient successfully created",
            PatientId = patient.Id
        };
    }
}
