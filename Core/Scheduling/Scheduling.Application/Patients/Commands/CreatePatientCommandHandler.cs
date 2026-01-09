using BuildingBlocks.Application;
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
        var patient = Patient.Create(request.FirstName, request.LastName, request.Email, request.DateOfBirth, request.PhoneNumber, request.Status);
        _uow.RepositoryFor<Patient>().Add(patient);

        // SaveChanges auto-dispatches domain events that were set in the behaviours inssued from the entity
        await _uow.SaveChangesAsync(cancellationToken);

        return new CreatePatientCommandResponse 
        { 
            Success = true, 
            Message = "Patient succesfully saved", 
            PatientDto = PatientDto.ToDto(patient) 
        };
    }
}   
