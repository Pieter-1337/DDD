using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Validators;
using FluentValidation;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries
{
    public record GetAllPatientsQuery : Query<IEnumerable<PatientDto>>
    {
        public PatientStatus Status { get; init; }
    }

    #region Validators
    internal class GetAllPatientsQueryValidator : UserValidator<GetAllPatientsQuery>
    {
        public GetAllPatientsQueryValidator()
        {
            RuleFor(q => q.Status)
                .IsInEnum()
                .WithMessage("Invalid patient status");
        }
    }
    #endregion Validators
}
