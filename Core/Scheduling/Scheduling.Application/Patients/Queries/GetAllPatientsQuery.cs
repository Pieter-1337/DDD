using BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Queries
{
    public class GetAllPatientsQuery : IRequest<IEnumerable<PatientDto>>
    {
        public PatientStatus Status { get; set; }
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
