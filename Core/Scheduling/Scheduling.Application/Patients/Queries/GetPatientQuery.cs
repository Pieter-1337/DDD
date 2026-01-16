using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

public record GetPatientQuery : Query<PatientDto?>
{
    public Guid Id { get; init; }
}

#region Validators
internal class GetPatientQueryValidator : UserValidator<GetPatientQuery>
{
    private readonly IUnitOfWork _uow;

    public GetPatientQueryValidator(IUnitOfWork uow)
    {
        _uow = uow;

        RuleFor(q => q.Id)
            .MustAsync(BeAValidPatientAsync)
            .WithErrorCode(ErrorCode.NotFound.Value)
            .WithMessage(ErrorCode.NotFound.Message);
    }

    private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
    {
        return await _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
    }
}
#endregion Validators
