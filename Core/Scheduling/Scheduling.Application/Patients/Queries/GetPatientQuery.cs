using BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries;

public class GetPatientQuery : IRequest<PatientDto?>
{
    public Guid Id { get; set; }
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
            .WithMessage("Patient not found");
    }

    private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
    {
        return await _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
    }
}
#endregion Validators
