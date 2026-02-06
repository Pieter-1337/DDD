using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries
{
    public record GetAllPatientsQuery : Query<IEnumerable<PatientDto>>
    {
        public string Status { get; init; }
    }

    #region Validators
    internal class GetAllPatientsQueryValidator : UserValidator<GetAllPatientsQuery>
    {
        public GetAllPatientsQueryValidator()
        {
            RuleFor(q => q.Status)
                .Must(PatientStatus.IsInEnum)
                .WithErrorCode(ErrorCode.InvalidStatus.Value)
                .WithMessage(ErrorCode.InvalidStatus.Message);
        }
    }
    #endregion Validators
}
