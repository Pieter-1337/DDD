using BuildingBlocks.Application.Interfaces;
using IntegrationEvents.Scheduling;
using MediatR;
using Scheduling.Application.Patients.Dtos;
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

        // Queue integration event to be published after successful save
        _uow.QueueIntegrationEvent(new PatientCreatedIntegrationEvent(
            patient.Id,
            patient.FirstName,
            patient.LastName,
            patient.Email,
            patient.DateOfBirth));

        await _uow.SaveChangesAsync(cancellationToken);

        return new CreatePatientCommandResponse
        {
            Success = true,
            Message = "Patient succesfully saved",
            PatientDto = PatientDto.ToDto(patient)
        };
    }
}   
