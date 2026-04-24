using Auth;
using BuildingBlocks.Application.Auth;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands
{
    public record ActivatePatientCommand : Command<ActivatePatientCommandResponse>
    {
        public Guid Id { get; init; }
    }

    public class ActivatePatientCommandResponse : SuccessOrFailureDto { }

    #region Validators
    internal class ActivatePatientCommandValidator : UserValidator<ActivatePatientCommand>
    {
        private readonly IUnitOfWork _uow;

        public ActivatePatientCommandValidator(ICurrentUser currentUser,IUnitOfWork uow)
            : base(currentUser, new[] { AppRoles.Doctor }, new[] { AppRoles.Admin })
        {
            _uow = uow;

            RuleFor(c => c.Id)
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
}
