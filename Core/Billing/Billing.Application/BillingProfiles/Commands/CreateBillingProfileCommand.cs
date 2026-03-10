using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Enumerations;
using Billing.Domain.BillingProfiles;
using FluentValidation;
using FluentValidation.Validators;

namespace Billing.Application.BillingProfiles.Commands
{
    public record CreateBillingProfileCommand(CreateBillingProfileRequest billingProfile) : Command<CreateBillingProfileCommandResponse>;

    public class CreateBillingProfileRequest
    {
        public Guid PatientId { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
    }

    public class CreateBillingProfileCommandResponse : SuccessOrFailureDto
    {
        public Guid BillingProfileId { get; set; }
    }


    #region Validators
    internal class CreateBillingProfileCommandValidator : AbstractValidator<CreateBillingProfileCommand>
    {
        public CreateBillingProfileCommandValidator(IValidator<CreateBillingProfileRequest> createBillingProfileRequestValidator)
        {
            RuleFor(c => c.billingProfile).Cascade(CascadeMode.Stop)
                .NotNull()
                .SetValidator(createBillingProfileRequestValidator);
        }
    }

    internal class CreateBillingProfileRequestValidator : AbstractValidator<CreateBillingProfileRequest>
    {
        private readonly IUnitOfWork _uow;

        public CreateBillingProfileRequestValidator(IUnitOfWork uow)
        {
            _uow = uow;

            RuleFor(x => x.PatientId)
                .NotEmpty()
                .WithErrorCode(ErrorCode.Required.Value)
                .WithMessage(ErrorCode.Required.Message)
                .MustAsync(NotAlreadyHaveBillingProfileAsync)
                .WithErrorCode(ErrorCode.Conflict.Value)
                .WithMessage("A billing profile already exists for this patient");

            RuleFor(x => x.Email)
                .NotEmpty()
                .WithErrorCode(ErrorCode.EmailRequired.Value)
                .WithMessage(ErrorCode.EmailRequired.Message)
                .EmailAddress(EmailValidationMode.AspNetCoreCompatible)
                .WithErrorCode(ErrorCode.InvalidEmail.Value)
                .WithMessage(ErrorCode.InvalidEmail.Message);

            RuleFor(x => x.FullName)
                .NotEmpty()
                .WithErrorCode(ErrorCode.Required.Value)
                .WithMessage(ErrorCode.Required.Message);
        }

        private async Task<bool> NotAlreadyHaveBillingProfileAsync(Guid patientId, CancellationToken ct)
        {
            return !await _uow.RepositoryFor<BillingProfile>().ExistsAsync(bp => bp.PatientId == patientId, ct);
        }
    }
    #endregion Validators
}
