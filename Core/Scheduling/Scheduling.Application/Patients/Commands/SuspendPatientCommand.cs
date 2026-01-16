using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands
{
    public record SuspendPatientCommand : Command<SuspendPatientCommandResponse>
    {
        public Guid Id { get; init; }
    }

    public class SuspendPatientCommandResponse : SuccessOrFailureDto { }

    #region Validators
    internal class SuspendPatientCommandValidator : UserValidator<SuspendPatientCommand>
    {
        private readonly IUnitOfWork _uow;

        public SuspendPatientCommandValidator(IUnitOfWork uow)
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
