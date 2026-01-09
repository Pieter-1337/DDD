using BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands
{
    public class SuspendPatientCommand : IRequest<SuspendPatientCommandResponse>
    {
        public Guid Id { get; set; }
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
                .WithMessage("Patient not found");
        }

        private async Task<bool> BeAValidPatientAsync(Guid id, CancellationToken ct)
        {
            return await _uow.RepositoryFor<Patient>().ExistsAsync(id, ct);
        }
    }
    #endregion Validators
}
